using System.Security.Claims;
using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.GraphQL.Private;
using HoldFast.Shared.Auth;
using HotChocolate;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HoldFast.Shared.Tests;

/// <summary>
/// Tests for workspace invite send, join workspace, session export,
/// dashboard definitions, and additional queries from commit #11.
/// </summary>
public class WorkspaceInviteAndExportTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly PrivateMutation _mutation;
    private readonly PrivateQuery _query;
    private readonly AuthorizationService _authz;
    private readonly ClaimsPrincipal _principal;
    private readonly Admin _admin;
    private readonly Workspace _workspace;
    private readonly Project _project;

    private static ClaimsPrincipal MakePrincipal(string uid) =>
        new(new ClaimsIdentity(new[]
        {
            new Claim(HoldFastClaimTypes.Uid, uid),
            new Claim(HoldFastClaimTypes.Email, $"{uid}@test.com"),
        }, "Test"));

    public WorkspaceInviteAndExportTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options;
        _db = new HoldFastDbContext(options);
        _db.Database.EnsureCreated();

        _admin = new Admin { Uid = "invite-uid", Name = "Invite Admin", Email = "invite@test.com" };
        _db.Admins.Add(_admin);
        _workspace = new Workspace { Name = "WS", PlanTier = "Enterprise" };
        _db.Workspaces.Add(_workspace);
        _db.SaveChanges();

        _db.WorkspaceAdmins.Add(new WorkspaceAdmin { AdminId = _admin.Id, WorkspaceId = _workspace.Id, Role = "ADMIN" });
        _project = new Project { Name = "Proj", WorkspaceId = _workspace.Id };
        _db.Projects.Add(_project);
        _db.SaveChanges();

        _authz = new AuthorizationService(_db);
        _principal = MakePrincipal("invite-uid");
        _mutation = new PrivateMutation();
        _query = new PrivateQuery();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    // ── SendAdminWorkspaceInvite ─────────────────────────────────────

    [Fact]
    public async Task SendAdminWorkspaceInvite_CreatesInviteLink()
    {
        var secret = await _mutation.SendAdminWorkspaceInvite(
            _workspace.Id, "newuser@test.com", "MEMBER", null,
            _principal, _authz, _db, CancellationToken.None);

        Assert.NotEmpty(secret);
        var invite = await _db.WorkspaceInviteLinks.FirstAsync();
        Assert.Equal("newuser@test.com", invite.InviteeEmail);
        Assert.Equal("MEMBER", invite.InviteeRole);
        Assert.Equal(_workspace.Id, invite.WorkspaceId);
        Assert.NotNull(invite.ExpirationDate);
    }

    [Fact]
    public async Task SendAdminWorkspaceInvite_WithProjectIds()
    {
        var secret = await _mutation.SendAdminWorkspaceInvite(
            _workspace.Id, "proj@test.com", "MEMBER", [_project.Id],
            _principal, _authz, _db, CancellationToken.None);

        var invite = await _db.WorkspaceInviteLinks.FirstAsync();
        Assert.NotNull(invite.ProjectIds);
        Assert.Contains(_project.Id, invite.ProjectIds!);
    }

    [Fact]
    public async Task SendAdminWorkspaceInvite_InvalidRole_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.SendAdminWorkspaceInvite(
                _workspace.Id, "x@test.com", "SUPERADMIN", null,
                _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task SendAdminWorkspaceInvite_DuplicateEmail_Throws()
    {
        await _mutation.SendAdminWorkspaceInvite(
            _workspace.Id, "dup@test.com", "MEMBER", null,
            _principal, _authz, _db, CancellationToken.None);

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.SendAdminWorkspaceInvite(
                _workspace.Id, "dup@test.com", "MEMBER", null,
                _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task SendAdminWorkspaceInvite_AlreadyMember_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.SendAdminWorkspaceInvite(
                _workspace.Id, "invite@test.com", "MEMBER", null,
                _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task SendAdminWorkspaceInvite_CaseInsensitiveEmailCheck()
    {
        await _mutation.SendAdminWorkspaceInvite(
            _workspace.Id, "UPPER@TEST.COM", "MEMBER", null,
            _principal, _authz, _db, CancellationToken.None);

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.SendAdminWorkspaceInvite(
                _workspace.Id, "upper@test.com", "MEMBER", null,
                _principal, _authz, _db, CancellationToken.None));
    }

    // ── JoinWorkspace ────────────────────────────────────────────────

    [Fact]
    public async Task JoinWorkspace_AddsAsMember()
    {
        var newAdmin = new Admin { Uid = "joiner", Email = "joiner@test.com" };
        _db.Admins.Add(newAdmin);
        await _db.SaveChangesAsync();

        var joinerPrincipal = MakePrincipal("joiner");
        var result = await _mutation.JoinWorkspace(
            _workspace.Id, joinerPrincipal, _authz, _db, CancellationToken.None);

        Assert.Equal(_workspace.Id, result);

        var wa = await _db.WorkspaceAdmins
            .FirstOrDefaultAsync(x => x.AdminId == newAdmin.Id && x.WorkspaceId == _workspace.Id);
        Assert.NotNull(wa);
        Assert.Equal("MEMBER", wa!.Role);
    }

    [Fact]
    public async Task JoinWorkspace_AlreadyMember_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.JoinWorkspace(_workspace.Id, _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task JoinWorkspace_NotFound_Throws()
    {
        var newAdmin = new Admin { Uid = "ghost", Email = "ghost@test.com" };
        _db.Admins.Add(newAdmin);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.JoinWorkspace(9999, MakePrincipal("ghost"), _authz, _db, CancellationToken.None));
    }

    // ── ExportSession ────────────────────────────────────────────────

    [Fact]
    public async Task ExportSession_CreatesExportRecord()
    {
        var session = new Session { ProjectId = _project.Id, SecureId = "abc-secure" };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        var result = await _mutation.ExportSession(
            "abc-secure", _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var export = await _db.SessionExports.FirstAsync();
        Assert.Equal(session.Id, export.SessionId);
        Assert.Equal("mp4", export.Type);
    }

    [Fact]
    public async Task ExportSession_Duplicate_ResetsExisting()
    {
        var session = new Session { ProjectId = _project.Id, SecureId = "dup-secure" };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        _db.SessionExports.Add(new SessionExport
        {
            SessionId = session.Id,
            Type = "mp4",
            Url = "old-url",
            Error = "old-error",
        });
        await _db.SaveChangesAsync();

        await _mutation.ExportSession("dup-secure", _principal, _authz, _db, CancellationToken.None);

        var export = await _db.SessionExports.FirstAsync();
        Assert.Null(export.Url);
        Assert.Null(export.Error);
        Assert.Equal(1, await _db.SessionExports.CountAsync()); // Still one record
    }

    [Fact]
    public async Task ExportSession_NotFound_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.ExportSession("nonexistent", _principal, _authz, _db, CancellationToken.None));
    }

    // ── Dashboard Definitions ────────────────────────────────────────

    [Fact]
    public async Task GetDashboardDefinitions_ReturnsWithMetrics()
    {
        var dashboard = new Dashboard
        {
            ProjectId = _project.Id,
            Name = "Test Dashboard",
            Metrics = [
                new DashboardMetric { Name = "CPU", Description = "CPU Usage", Aggregator = "P50" },
                new DashboardMetric { Name = "Memory", Description = "Memory Usage", Aggregator = "AVG" },
            ]
        };
        _db.Dashboards.Add(dashboard);
        await _db.SaveChangesAsync();

        var results = await _query.GetDashboardDefinitions(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("Test Dashboard", results[0].Name);
        Assert.Equal(2, results[0].Metrics.Count);
    }

    [Fact]
    public async Task GetDashboardDefinitions_Empty_ReturnsEmptyList()
    {
        var results = await _query.GetDashboardDefinitions(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Empty(results);
    }

    [Fact]
    public async Task GetDashboardDefinitions_MultipleDashboards_OrderedByUpdatedAt()
    {
        _db.Dashboards.AddRange(
            new Dashboard { ProjectId = _project.Id, Name = "Old", UpdatedAt = DateTime.UtcNow.AddDays(-2) },
            new Dashboard { ProjectId = _project.Id, Name = "New", UpdatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var results = await _query.GetDashboardDefinitions(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Equal("New", results[0].Name);
    }

    // ── GetErrorsKeys edge cases ─────────────────────────────────────

    [Fact]
    public async Task GetErrorsKeys_EmptyQuery_ReturnsAll()
    {
        var keys = await _query.GetErrorsKeys(
            _project.Id, "", _principal, _authz, CancellationToken.None);

        Assert.True(keys.Count >= 10);
    }

    // ── GetEnhancedUserDetails ───────────────────────────────────────

    [Fact]
    public async Task GetEnhancedUserDetails_Found()
    {
        _db.EnhancedUserDetails.Add(new EnhancedUserDetails
        {
            Email = "ENHANCED@TEST.COM",
            PersonJson = "{\"name\":\"Test User\"}",
        });
        await _db.SaveChangesAsync();

        var result = await _query.GetEnhancedUserDetails(
            "enhanced@test.com", _principal, _authz, _db, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("Test User", result!.PersonJson!);
    }

    [Fact]
    public async Task GetEnhancedUserDetails_NotFound()
    {
        var result = await _query.GetEnhancedUserDetails(
            "nobody@test.com", _principal, _authz, _db, CancellationToken.None);

        Assert.Null(result);
    }
}
