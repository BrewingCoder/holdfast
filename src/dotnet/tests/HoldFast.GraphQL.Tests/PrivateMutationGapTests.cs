using System.Security.Claims;
using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.Domain.Enums;
using HoldFast.GraphQL.Private;
using HoldFast.Shared.Auth;
using HotChocolate;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HoldFast.GraphQL.Tests;

/// <summary>
/// Tests for previously untested PrivateMutation methods:
/// SaveBillingPlan, DeleteSessions, UpdateAllowedEmailOrigins,
/// ChangeProjectMembership, ReplyToErrorComment, UpdateMetricMonitorIsDisabled.
/// </summary>
public class PrivateMutationGapTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly AuthorizationService _authz;
    private readonly PrivateMutation _mutation;
    private readonly ClaimsPrincipal _principal;
    private readonly Admin _admin;
    private readonly Workspace _workspace;
    private readonly Project _project;

    public PrivateMutationGapTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options;
        _db = new HoldFastDbContext(options);
        _db.Database.EnsureCreated();
        _mutation = new PrivateMutation();

        _admin = new Admin { Uid = "admin-1", Email = "admin@test.com" };
        _db.Admins.Add(_admin);
        _workspace = new Workspace { Name = "WS" };
        _db.Workspaces.Add(_workspace);
        _db.SaveChanges();

        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
            { AdminId = _admin.Id, WorkspaceId = _workspace.Id, Role = "ADMIN" });
        _project = new Project { Name = "Proj", WorkspaceId = _workspace.Id };
        _db.Projects.Add(_project);
        _db.SaveChanges();

        _authz = new AuthorizationService(_db);
        _principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(HoldFastClaimTypes.Uid, "admin-1"),
            new Claim(HoldFastClaimTypes.AdminId, _admin.Id.ToString()),
        }, "Test"));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // ── SaveBillingPlan ─────────────────────────────────────────────────

    [Fact]
    public async Task SaveBillingPlan_UpdatesAllRetentionPeriods()
    {
        var result = await _mutation.SaveBillingPlan(
            _workspace.Id,
            RetentionPeriod.TwelveMonths,
            RetentionPeriod.ThreeMonths,
            RetentionPeriod.SevenDays,
            RetentionPeriod.ThirtyDays,
            RetentionPeriod.TwoYears,
            _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var ws = await _db.Workspaces.FindAsync(_workspace.Id);
        Assert.Equal(RetentionPeriod.TwelveMonths, ws!.RetentionPeriod);
        Assert.Equal(RetentionPeriod.ThreeMonths, ws.ErrorsRetentionPeriod);
        Assert.Equal(RetentionPeriod.SevenDays, ws.LogsRetentionPeriod);
        Assert.Equal(RetentionPeriod.ThirtyDays, ws.TracesRetentionPeriod);
        Assert.Equal(RetentionPeriod.TwoYears, ws.MetricsRetentionPeriod);
    }

    [Fact]
    public async Task SaveBillingPlan_NonexistentWorkspace_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.SaveBillingPlan(
                99999,
                RetentionPeriod.ThirtyDays,
                RetentionPeriod.ThirtyDays,
                RetentionPeriod.ThirtyDays,
                RetentionPeriod.ThirtyDays,
                RetentionPeriod.ThirtyDays,
                _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task SaveBillingPlan_CanSetSameValueTwice()
    {
        await _mutation.SaveBillingPlan(
            _workspace.Id,
            RetentionPeriod.ThreeYears,
            RetentionPeriod.ThreeYears,
            RetentionPeriod.ThreeYears,
            RetentionPeriod.ThreeYears,
            RetentionPeriod.ThreeYears,
            _principal, _authz, _db, CancellationToken.None);

        // Call again with same values — should be idempotent
        var result = await _mutation.SaveBillingPlan(
            _workspace.Id,
            RetentionPeriod.ThreeYears,
            RetentionPeriod.ThreeYears,
            RetentionPeriod.ThreeYears,
            RetentionPeriod.ThreeYears,
            RetentionPeriod.ThreeYears,
            _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task SaveBillingPlan_PreservesOtherWorkspaceFields()
    {
        _workspace.Name = "Important WS";
        _workspace.SlackAccessToken = "slack-token";
        await _db.SaveChangesAsync();

        await _mutation.SaveBillingPlan(
            _workspace.Id,
            RetentionPeriod.SevenDays,
            RetentionPeriod.SevenDays,
            RetentionPeriod.SevenDays,
            RetentionPeriod.SevenDays,
            RetentionPeriod.SevenDays,
            _principal, _authz, _db, CancellationToken.None);

        var ws = await _db.Workspaces.FindAsync(_workspace.Id);
        Assert.Equal("Important WS", ws!.Name);
        Assert.Equal("slack-token", ws.SlackAccessToken);
    }

    // ── DeleteSessions ──────────────────────────────────────────────────

    [Fact]
    public async Task DeleteSessions_CreatesTask()
    {
        var result = await _mutation.DeleteSessions(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var task = await _db.DeleteSessionsTasks.FirstOrDefaultAsync(t => t.ProjectId == _project.Id);
        Assert.NotNull(task);
        Assert.Equal(_project.Id, task.ProjectId);
    }

    [Fact]
    public async Task DeleteSessions_NonexistentProject_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.DeleteSessions(
                99999, _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteSessions_MultipleCallsCreateMultipleTasks()
    {
        await _mutation.DeleteSessions(_project.Id, _principal, _authz, _db, CancellationToken.None);
        await _mutation.DeleteSessions(_project.Id, _principal, _authz, _db, CancellationToken.None);

        var tasks = await _db.DeleteSessionsTasks.Where(t => t.ProjectId == _project.Id).ToListAsync();
        Assert.Equal(2, tasks.Count);
    }

    // ── UpdateAllowedEmailOrigins ────────────────────────────────────────

    [Fact]
    public async Task UpdateAllowedEmailOrigins_SetsOrigins()
    {
        var result = await _mutation.UpdateAllowedEmailOrigins(
            _workspace.Id,
            new List<string> { "example.com", "test.org" },
            _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var ws = await _db.Workspaces.FindAsync(_workspace.Id);
        Assert.Equal("example.com,test.org", ws!.AllowedAutoJoinEmailOrigins);
    }

    [Fact]
    public async Task UpdateAllowedEmailOrigins_NullClearsOrigins()
    {
        // Set first
        _workspace.AllowedAutoJoinEmailOrigins = "old.com";
        await _db.SaveChangesAsync();

        var result = await _mutation.UpdateAllowedEmailOrigins(
            _workspace.Id, null, _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var ws = await _db.Workspaces.FindAsync(_workspace.Id);
        Assert.Null(ws!.AllowedAutoJoinEmailOrigins);
    }

    [Fact]
    public async Task UpdateAllowedEmailOrigins_EmptyListSetsEmpty()
    {
        var result = await _mutation.UpdateAllowedEmailOrigins(
            _workspace.Id,
            new List<string>(),
            _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var ws = await _db.Workspaces.FindAsync(_workspace.Id);
        Assert.Equal("", ws!.AllowedAutoJoinEmailOrigins);
    }

    [Fact]
    public async Task UpdateAllowedEmailOrigins_SingleOrigin()
    {
        var result = await _mutation.UpdateAllowedEmailOrigins(
            _workspace.Id,
            new List<string> { "company.com" },
            _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var ws = await _db.Workspaces.FindAsync(_workspace.Id);
        Assert.Equal("company.com", ws!.AllowedAutoJoinEmailOrigins);
    }

    [Fact]
    public async Task UpdateAllowedEmailOrigins_NonexistentWorkspace_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.UpdateAllowedEmailOrigins(
                99999, new List<string> { "x.com" },
                _principal, _authz, _db, CancellationToken.None));
    }

    // ── ChangeProjectMembership ──────────────────────────────────────────

    [Fact]
    public async Task ChangeProjectMembership_SetsProjectIds()
    {
        var project2 = new Project { Name = "P2", WorkspaceId = _workspace.Id };
        _db.Projects.Add(project2);
        await _db.SaveChangesAsync();

        var result = await _mutation.ChangeProjectMembership(
            _workspace.Id, _admin.Id,
            new List<int> { _project.Id, project2.Id },
            _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var wa = await _db.WorkspaceAdmins
            .FirstAsync(wa => wa.AdminId == _admin.Id && wa.WorkspaceId == _workspace.Id);
        Assert.NotNull(wa.ProjectIds);
        Assert.Equal(2, wa.ProjectIds.Count);
        Assert.Contains(_project.Id, wa.ProjectIds);
        Assert.Contains(project2.Id, wa.ProjectIds);
    }

    [Fact]
    public async Task ChangeProjectMembership_NullClearsProjectIds()
    {
        // Set project IDs first
        var wa = await _db.WorkspaceAdmins
            .FirstAsync(wa => wa.AdminId == _admin.Id && wa.WorkspaceId == _workspace.Id);
        wa.ProjectIds = new List<int> { _project.Id };
        await _db.SaveChangesAsync();

        var result = await _mutation.ChangeProjectMembership(
            _workspace.Id, _admin.Id, null,
            _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var updated = await _db.WorkspaceAdmins
            .FirstAsync(wa2 => wa2.AdminId == _admin.Id && wa2.WorkspaceId == _workspace.Id);
        Assert.Null(updated.ProjectIds);
    }

    [Fact]
    public async Task ChangeProjectMembership_AdminNotInWorkspace_Throws()
    {
        var otherAdmin = new Admin { Uid = "other", Email = "other@test.com" };
        _db.Admins.Add(otherAdmin);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.ChangeProjectMembership(
                _workspace.Id, otherAdmin.Id,
                new List<int> { _project.Id },
                _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task ChangeProjectMembership_EmptyListSetsEmpty()
    {
        var result = await _mutation.ChangeProjectMembership(
            _workspace.Id, _admin.Id,
            new List<int>(),
            _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var wa = await _db.WorkspaceAdmins
            .FirstAsync(wa => wa.AdminId == _admin.Id && wa.WorkspaceId == _workspace.Id);
        Assert.NotNull(wa.ProjectIds);
        Assert.Empty(wa.ProjectIds);
    }

    // ── ReplyToErrorComment ──────────────────────────────────────────────

    [Fact]
    public async Task ReplyToErrorComment_CreatesReply()
    {
        var errorGroup = new ErrorGroup
        {
            ProjectId = _project.Id, Event = "err", SecureId = "eg1",
            State = ErrorGroupState.Open
        };
        _db.ErrorGroups.Add(errorGroup);
        await _db.SaveChangesAsync();

        var comment = new ErrorComment
        {
            ErrorGroupId = errorGroup.Id, AdminId = _admin.Id, Text = "Bug here"
        };
        _db.ErrorComments.Add(comment);
        await _db.SaveChangesAsync();

        var reply = await _mutation.ReplyToErrorComment(
            comment.Id, "I agree",
            _principal, _authz, _db, CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Equal(comment.Id, reply.ErrorCommentId);
        Assert.Equal(_admin.Id, reply.AdminId);
        Assert.Equal("I agree", reply.Text);
    }

    [Fact]
    public async Task ReplyToErrorComment_NonexistentComment_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.ReplyToErrorComment(
                99999, "reply text",
                _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task ReplyToErrorComment_MultipleReplies()
    {
        var errorGroup = new ErrorGroup
        {
            ProjectId = _project.Id, Event = "err", SecureId = "eg2",
            State = ErrorGroupState.Open
        };
        _db.ErrorGroups.Add(errorGroup);
        await _db.SaveChangesAsync();

        var comment = new ErrorComment
        {
            ErrorGroupId = errorGroup.Id, AdminId = _admin.Id, Text = "First"
        };
        _db.ErrorComments.Add(comment);
        await _db.SaveChangesAsync();

        var reply1 = await _mutation.ReplyToErrorComment(
            comment.Id, "Reply 1", _principal, _authz, _db, CancellationToken.None);
        var reply2 = await _mutation.ReplyToErrorComment(
            comment.Id, "Reply 2", _principal, _authz, _db, CancellationToken.None);

        Assert.NotEqual(reply1.Id, reply2.Id);
        var replies = await _db.CommentReplies
            .Where(r => r.ErrorCommentId == comment.Id).ToListAsync();
        Assert.Equal(2, replies.Count);
    }

    [Fact]
    public async Task ReplyToErrorComment_EmptyText()
    {
        var errorGroup = new ErrorGroup
        {
            ProjectId = _project.Id, Event = "err", SecureId = "eg3",
            State = ErrorGroupState.Open
        };
        _db.ErrorGroups.Add(errorGroup);
        await _db.SaveChangesAsync();

        var comment = new ErrorComment
        {
            ErrorGroupId = errorGroup.Id, AdminId = _admin.Id, Text = "Bug"
        };
        _db.ErrorComments.Add(comment);
        await _db.SaveChangesAsync();

        var reply = await _mutation.ReplyToErrorComment(
            comment.Id, "", _principal, _authz, _db, CancellationToken.None);

        Assert.Equal("", reply.Text);
    }

    // ── UpdateMetricMonitorIsDisabled ─────────────────────────────────────

    [Fact]
    public async Task UpdateMetricMonitorIsDisabled_DisablesMonitor()
    {
        var monitor = new MetricMonitor
        {
            ProjectId = _project.Id, Name = "Latency P50",
            MetricToMonitor = "http.request.duration", Disabled = false
        };
        _db.MetricMonitors.Add(monitor);
        await _db.SaveChangesAsync();

        var result = await _mutation.UpdateMetricMonitorIsDisabled(
            monitor.Id, _project.Id, true,
            _principal, _authz, _db, CancellationToken.None);

        Assert.True(result.Disabled);
        Assert.Equal("Latency P50", result.Name);
    }

    [Fact]
    public async Task UpdateMetricMonitorIsDisabled_EnablesMonitor()
    {
        var monitor = new MetricMonitor
        {
            ProjectId = _project.Id, Name = "Error Rate",
            MetricToMonitor = "error.count", Disabled = true
        };
        _db.MetricMonitors.Add(monitor);
        await _db.SaveChangesAsync();

        var result = await _mutation.UpdateMetricMonitorIsDisabled(
            monitor.Id, _project.Id, false,
            _principal, _authz, _db, CancellationToken.None);

        Assert.False(result.Disabled);
    }

    [Fact]
    public async Task UpdateMetricMonitorIsDisabled_NonexistentMonitor_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.UpdateMetricMonitorIsDisabled(
                99999, _project.Id, true,
                _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateMetricMonitorIsDisabled_WrongProject_Throws()
    {
        var monitor = new MetricMonitor
        {
            ProjectId = _project.Id, Name = "Memory",
            MetricToMonitor = "process.memory", Disabled = false
        };
        _db.MetricMonitors.Add(monitor);
        await _db.SaveChangesAsync();

        var otherProject = new Project { Name = "Other", WorkspaceId = _workspace.Id };
        _db.Projects.Add(otherProject);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.UpdateMetricMonitorIsDisabled(
                monitor.Id, otherProject.Id, true,
                _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateMetricMonitorIsDisabled_Idempotent()
    {
        var monitor = new MetricMonitor
        {
            ProjectId = _project.Id, Name = "CPU",
            MetricToMonitor = "cpu.usage", Disabled = true
        };
        _db.MetricMonitors.Add(monitor);
        await _db.SaveChangesAsync();

        // Disable again — should be a no-op
        var result = await _mutation.UpdateMetricMonitorIsDisabled(
            monitor.Id, _project.Id, true,
            _principal, _authz, _db, CancellationToken.None);

        Assert.True(result.Disabled);
    }
}
