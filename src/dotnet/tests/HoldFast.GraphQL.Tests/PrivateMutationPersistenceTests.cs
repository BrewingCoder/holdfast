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
/// Tests verifying that mutations correctly persist changes to the database.
/// Uses a second DbContext to read back values, ensuring changes aren't just
/// in-memory on the tracked entity.
/// </summary>
public class PrivateMutationPersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<HoldFastDbContext> _options;
    private readonly HoldFastDbContext _db;
    private readonly AuthorizationService _authz;
    private readonly PrivateMutation _mutation;
    private readonly ClaimsPrincipal _principal;
    private readonly Admin _admin;
    private readonly Workspace _workspace;
    private readonly Project _project;

    public PrivateMutationPersistenceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options;
        _db = new HoldFastDbContext(_options);
        _db.Database.EnsureCreated();
        _mutation = new PrivateMutation();

        _admin = new Admin { Uid = "admin-1", Email = "admin@test.com", Name = "Original" };
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

    /// <summary>
    /// Read back from a fresh DbContext to verify true persistence (not just EF tracking).
    /// </summary>
    private HoldFastDbContext CreateFreshContext() => new(_options);

    // ── UpdateAdminAboutYouDetails persistence ───────────────────────────

    [Fact]
    public async Task UpdateAdminAboutYouDetails_PersistsToDatabase()
    {
        await _mutation.UpdateAdminAboutYouDetails(
            "Persisted Name", "referral-source", "SRE",
            _principal, _authz, _db, CancellationToken.None);

        // Read from a fresh context to verify it actually hit the DB
        using var fresh = CreateFreshContext();
        var admin = await fresh.Admins.FindAsync(_admin.Id);
        Assert.Equal("Persisted Name", admin!.Name);
        Assert.Equal("referral-source", admin.Referral);
        Assert.Equal("SRE", admin.UserDefinedRole);
    }

    [Fact]
    public async Task UpdateAdminAboutYouDetails_NullFieldsNotOverwritten()
    {
        _admin.Name = "Keep This";
        _admin.Referral = "Keep Referral";
        _admin.UserDefinedRole = "Keep Role";
        await _db.SaveChangesAsync();

        await _mutation.UpdateAdminAboutYouDetails(
            null, null, null,
            _principal, _authz, _db, CancellationToken.None);

        using var fresh = CreateFreshContext();
        var admin = await fresh.Admins.FindAsync(_admin.Id);
        Assert.Equal("Keep This", admin!.Name);
        Assert.Equal("Keep Referral", admin.Referral);
        Assert.Equal("Keep Role", admin.UserDefinedRole);
    }

    // ── CreateWorkspace persistence ──────────────────────────────────────

    [Fact]
    public async Task CreateWorkspace_PersistsWithSettings()
    {
        var ws = await _mutation.CreateWorkspace(
            "New WS", _principal, _authz, _db, CancellationToken.None);

        using var fresh = CreateFreshContext();
        var persisted = await fresh.Workspaces.FindAsync(ws.Id);
        Assert.NotNull(persisted);
        Assert.Equal("New WS", persisted!.Name);
        Assert.Equal("Enterprise", persisted.PlanTier);

        var settings = await fresh.AllWorkspaceSettings
            .FirstOrDefaultAsync(s => s.WorkspaceId == ws.Id);
        Assert.NotNull(settings);
        Assert.True(settings!.AIApplication);
        Assert.True(settings.EnableUnlimitedSeats);
    }

    // ── EditWorkspace persistence ────────────────────────────────────────

    [Fact]
    public async Task EditWorkspace_PersistsName()
    {
        await _mutation.EditWorkspace(
            _workspace.Id, "Renamed WS", _principal, _authz, _db, CancellationToken.None);

        using var fresh = CreateFreshContext();
        var ws = await fresh.Workspaces.FindAsync(_workspace.Id);
        Assert.Equal("Renamed WS", ws!.Name);
    }

    // ── SaveBillingPlan persistence ──────────────────────────────────────

    [Fact]
    public async Task SaveBillingPlan_PersistsRetentionPeriods()
    {
        await _mutation.SaveBillingPlan(
            _workspace.Id,
            RetentionPeriod.ThreeYears,
            RetentionPeriod.TwoYears,
            RetentionPeriod.TwelveMonths,
            RetentionPeriod.SixMonths,
            RetentionPeriod.ThreeMonths,
            _principal, _authz, _db, CancellationToken.None);

        using var fresh = CreateFreshContext();
        var ws = await fresh.Workspaces.FindAsync(_workspace.Id);
        Assert.Equal(RetentionPeriod.ThreeYears, ws!.RetentionPeriod);
        Assert.Equal(RetentionPeriod.TwoYears, ws.ErrorsRetentionPeriod);
        Assert.Equal(RetentionPeriod.TwelveMonths, ws.LogsRetentionPeriod);
        Assert.Equal(RetentionPeriod.SixMonths, ws.TracesRetentionPeriod);
        Assert.Equal(RetentionPeriod.ThreeMonths, ws.MetricsRetentionPeriod);
    }

    // ── UpdateErrorGroupState persistence ────────────────────────────────

    [Fact]
    public async Task UpdateErrorGroupState_PersistsStateAndActivityLog()
    {
        var eg = new ErrorGroup
        {
            ProjectId = _project.Id, Event = "test", SecureId = "eg-persist",
            State = ErrorGroupState.Open
        };
        _db.ErrorGroups.Add(eg);
        await _db.SaveChangesAsync();

        await _mutation.UpdateErrorGroupState(
            eg.Id, ErrorGroupState.Resolved,
            _principal, _authz, _db, CancellationToken.None);

        using var fresh = CreateFreshContext();
        var persisted = await fresh.ErrorGroups.FindAsync(eg.Id);
        Assert.Equal(ErrorGroupState.Resolved, persisted!.State);

        var log = await fresh.ErrorGroupActivityLogs
            .FirstOrDefaultAsync(l => l.ErrorGroupId == eg.Id);
        Assert.NotNull(log);
        Assert.Equal("Resolved", log!.Action);
        Assert.Equal(_admin.Id, log.AdminId);
    }

    // ── CreateProject persistence ────────────────────────────────────────

    [Fact]
    public async Task CreateProject_PersistsWithFilterSettings()
    {
        var proj = await _mutation.CreateProject(
            _workspace.Id, "Persisted Project",
            _principal, _authz, _db, CancellationToken.None);

        using var fresh = CreateFreshContext();
        var persisted = await fresh.Projects.FindAsync(proj.Id);
        Assert.NotNull(persisted);
        Assert.Equal("Persisted Project", persisted!.Name);
        Assert.Equal(_workspace.Id, persisted.WorkspaceId);

        var filterSettings = await fresh.ProjectFilterSettings
            .FirstOrDefaultAsync(fs => fs.ProjectId == proj.Id);
        Assert.NotNull(filterSettings);
        Assert.Equal(1.0, filterSettings!.SessionSamplingRate);
    }

    // ── DeleteSessions persistence ───────────────────────────────────────

    [Fact]
    public async Task DeleteSessions_PersistsTask()
    {
        await _mutation.DeleteSessions(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        using var fresh = CreateFreshContext();
        var task = await fresh.DeleteSessionsTasks
            .FirstOrDefaultAsync(t => t.ProjectId == _project.Id);
        Assert.NotNull(task);
    }

    // ── CreateSessionComment persistence ─────────────────────────────────

    [Fact]
    public async Task CreateSessionComment_PersistsAllFields()
    {
        var session = new Session
        {
            SecureId = "sess-comment-persist", ProjectId = _project.Id,
            CreatedAt = DateTime.UtcNow
        };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        var comment = await _mutation.CreateSessionComment(
            _project.Id, session.Id, "Test comment", 5000,
            100.5, 200.3,
            _principal, _authz, _db, CancellationToken.None);

        using var fresh = CreateFreshContext();
        var persisted = await fresh.SessionComments.FindAsync(comment.Id);
        Assert.NotNull(persisted);
        Assert.Equal("Test comment", persisted!.Text);
        Assert.Equal(5000, persisted.Timestamp);
        Assert.Equal(100.5, persisted.XCoordinate);
        Assert.Equal(200.3, persisted.YCoordinate);
        Assert.Equal("ADMIN", persisted.Type);
    }

    // ── ChangeProjectMembership persistence ──────────────────────────────

    [Fact]
    public async Task ChangeProjectMembership_PersistsProjectIds()
    {
        var p2 = new Project { Name = "P2", WorkspaceId = _workspace.Id };
        _db.Projects.Add(p2);
        await _db.SaveChangesAsync();

        await _mutation.ChangeProjectMembership(
            _workspace.Id, _admin.Id,
            new List<int> { _project.Id, p2.Id },
            _principal, _authz, _db, CancellationToken.None);

        using var fresh = CreateFreshContext();
        var wa = await fresh.WorkspaceAdmins
            .FirstAsync(wa => wa.AdminId == _admin.Id && wa.WorkspaceId == _workspace.Id);
        Assert.NotNull(wa.ProjectIds);
        Assert.Contains(_project.Id, wa.ProjectIds!);
        Assert.Contains(p2.Id, wa.ProjectIds!);
    }

    // ── UpdateAllowedEmailOrigins persistence ────────────────────────────

    [Fact]
    public async Task UpdateAllowedEmailOrigins_PersistsOrigins()
    {
        await _mutation.UpdateAllowedEmailOrigins(
            _workspace.Id,
            new List<string> { "company.com", "partner.org" },
            _principal, _authz, _db, CancellationToken.None);

        using var fresh = CreateFreshContext();
        var ws = await fresh.Workspaces.FindAsync(_workspace.Id);
        Assert.Equal("company.com,partner.org", ws!.AllowedAutoJoinEmailOrigins);
    }

    // ── UpsertDashboard persistence ──────────────────────────────────────

    [Fact]
    public async Task UpsertDashboard_PersistsNewDashboard()
    {
        var dashboard = await _mutation.UpsertDashboard(
            _project.Id, "My Dashboard", null,
            _principal, _authz, _db, CancellationToken.None);

        using var fresh = CreateFreshContext();
        var persisted = await fresh.Dashboards.FindAsync(dashboard.Id);
        Assert.NotNull(persisted);
        Assert.Equal("My Dashboard", persisted!.Name);
        Assert.Equal(_project.Id, persisted.ProjectId);
    }

    // ── ReplyToErrorComment persistence ──────────────────────────────────

    [Fact]
    public async Task ReplyToErrorComment_PersistsReply()
    {
        var eg = new ErrorGroup
        {
            ProjectId = _project.Id, Event = "err", SecureId = "eg-reply-persist",
            State = ErrorGroupState.Open
        };
        _db.ErrorGroups.Add(eg);
        await _db.SaveChangesAsync();

        var comment = new ErrorComment
        {
            ErrorGroupId = eg.Id, AdminId = _admin.Id, Text = "Original"
        };
        _db.ErrorComments.Add(comment);
        await _db.SaveChangesAsync();

        var reply = await _mutation.ReplyToErrorComment(
            comment.Id, "My reply",
            _principal, _authz, _db, CancellationToken.None);

        using var fresh = CreateFreshContext();
        var persisted = await fresh.CommentReplies.FindAsync(reply.Id);
        Assert.NotNull(persisted);
        Assert.Equal("My reply", persisted!.Text);
        Assert.Equal(comment.Id, persisted.ErrorCommentId);
        Assert.Equal(_admin.Id, persisted.AdminId);
    }
}
