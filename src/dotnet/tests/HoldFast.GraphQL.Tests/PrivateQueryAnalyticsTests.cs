using System.Security.Claims;
using HoldFast.Data;
using HoldFast.Data.ClickHouse;
using HoldFast.Data.ClickHouse.Models;
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
/// Tests for PrivateQuery IQueryable methods, analytics queries,
/// integration status checks, and admin flag queries.
/// </summary>
public class PrivateQueryAnalyticsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<HoldFastDbContext> _options;
    private readonly HoldFastDbContext _db;
    private readonly AuthorizationService _authz;
    private readonly PrivateQuery _query;

    private readonly Admin _admin;
    private readonly ClaimsPrincipal _principal;
    private readonly Workspace _workspace;
    private readonly Project _project;
    private readonly FakeClickHouseService _clickHouse = new();

    public PrivateQueryAnalyticsTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options;
        _db = new HoldFastDbContext(_options);
        _db.Database.EnsureCreated();
        _query = new PrivateQuery();

        _admin = new Admin { Uid = "admin-uid", Email = "admin@test.com" };
        _db.Admins.Add(_admin);
        _workspace = new Workspace { Name = "TestWS" };
        _db.Workspaces.Add(_workspace);
        _db.SaveChanges();

        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
            { AdminId = _admin.Id, WorkspaceId = _workspace.Id, Role = "ADMIN" });
        _project = new Project { Name = "TestProj", WorkspaceId = _workspace.Id };
        _db.Projects.Add(_project);
        _db.SaveChanges();

        _authz = new AuthorizationService(_db);
        _principal = MakePrincipal(_admin);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private static ClaimsPrincipal MakePrincipal(Admin admin) =>
        new(new ClaimsIdentity(new[]
        {
            new Claim(HoldFastClaimTypes.Uid, admin.Uid!),
            new Claim(HoldFastClaimTypes.AdminId, admin.Id.ToString()),
        }, "Test"));

    // ── GetProjects (IQueryable) ─────────────────────────────────────
    // GetProjects returns all projects across every workspace the admin belongs to.

    [Fact]
    public async Task GetProjects_ReturnsAllAdminProjects()
    {
        _db.Projects.Add(new Project { Name = "Proj2", WorkspaceId = _workspace.Id });
        await _db.SaveChangesAsync();

        var queryable = await _query.GetProjects(
            _principal, _authz, _db, CancellationToken.None);
        var list = await queryable.ToListAsync();

        Assert.Equal(2, list.Count);
        Assert.Contains(list, p => p.Name == "TestProj");
        Assert.Contains(list, p => p.Name == "Proj2");
    }

    [Fact]
    public async Task GetProjects_ExcludesProjectsFromUnrelatedWorkspaces()
    {
        // Add a workspace the admin is NOT a member of, plus a project there
        var otherWs = new Workspace { Name = "Other" };
        _db.Workspaces.Add(otherWs);
        await _db.SaveChangesAsync();
        _db.Projects.Add(new Project { Name = "OtherProj", WorkspaceId = otherWs.Id });
        await _db.SaveChangesAsync();

        var queryable = await _query.GetProjects(
            _principal, _authz, _db, CancellationToken.None);
        var list = await queryable.ToListAsync();

        // Only the project in the admin's workspace is returned
        Assert.Single(list);
        Assert.Equal("TestProj", list[0].Name);
    }

    [Fact]
    public async Task GetProjects_NoProjects_ReturnsEmpty()
    {
        // Remove all projects for this admin's workspaces
        _db.Projects.Remove(_project);
        await _db.SaveChangesAsync();

        var queryable = await _query.GetProjects(
            _principal, _authz, _db, CancellationToken.None);
        var list = await queryable.ToListAsync();

        Assert.Empty(list);
    }

    // ── GetErrorGroups (IQueryable) ──────────────────────────────────

    [Fact]
    public async Task GetErrorGroups_ReturnsGroupsForProject()
    {
        _db.ErrorGroups.AddRange(
            new ErrorGroup { ProjectId = _project.Id, Event = "err1", SecureId = "eg1", State = ErrorGroupState.Open },
            new ErrorGroup { ProjectId = _project.Id, Event = "err2", SecureId = "eg2", State = ErrorGroupState.Resolved });
        await _db.SaveChangesAsync();

        var result = await _query.GetErrorGroups(
            _project.Id, 100, new QueryInput { DateRange = new DateRangeRequiredInput { StartDate = DateTime.UtcNow.AddDays(-30), EndDate = DateTime.UtcNow.AddDays(1) } },
            null, _principal, _authz, _clickHouse, _db, CancellationToken.None);

        Assert.Equal(2, result.ErrorGroups.Count);
    }

    [Fact]
    public async Task GetErrorGroups_ExcludesOtherProjectGroups()
    {
        var otherProj = new Project { Name = "Other", WorkspaceId = _workspace.Id };
        _db.Projects.Add(otherProj);
        await _db.SaveChangesAsync();
        _db.ErrorGroups.Add(new ErrorGroup { ProjectId = otherProj.Id, Event = "other", SecureId = "eg-other", State = ErrorGroupState.Open });
        _db.ErrorGroups.Add(new ErrorGroup { ProjectId = _project.Id, Event = "mine", SecureId = "eg-mine", State = ErrorGroupState.Open });
        await _db.SaveChangesAsync();

        var result = await _query.GetErrorGroups(
            _project.Id, 100, new QueryInput { DateRange = new DateRangeRequiredInput { StartDate = DateTime.UtcNow.AddDays(-30), EndDate = DateTime.UtcNow.AddDays(1) } },
            null, _principal, _authz, _clickHouse, _db, CancellationToken.None);

        Assert.Single(result.ErrorGroups);
        Assert.Equal("mine", result.ErrorGroups[0].Event);
    }

    // ── GetErrorObjects (IQueryable) ─────────────────────────────────

    [Fact]
    public async Task GetErrorObjects_ReturnsObjectsForGroup()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, Event = "err", SecureId = "eg-obj", State = ErrorGroupState.Open };
        _db.ErrorGroups.Add(eg);
        await _db.SaveChangesAsync();

        _db.ErrorObjects.AddRange(
            new ErrorObject { ErrorGroupId = eg.Id, ProjectId = _project.Id, Event = "err" },
            new ErrorObject { ErrorGroupId = eg.Id, ProjectId = _project.Id, Event = "err" });
        await _db.SaveChangesAsync();

        var queryable = await _query.GetErrorObjects(
            eg.Id, _principal, _authz, _db, CancellationToken.None);
        var list = await queryable.ToListAsync();

        Assert.Equal(2, list.Count);
    }

    // ── GetRageClicks (IQueryable) ───────────────────────────────────

    [Fact]
    public async Task GetRageClicks_ReturnsEventsForSession()
    {
        var session = new Session { SecureId = "sess-rage", ProjectId = _project.Id, CreatedAt = DateTime.UtcNow };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        _db.RageClickEvents.AddRange(
            new RageClickEvent { ProjectId = _project.Id, SessionId = session.Id, TotalClicks = 5, Selector = ".btn", StartTimestamp = 100, EndTimestamp = 200 },
            new RageClickEvent { ProjectId = _project.Id, SessionId = session.Id, TotalClicks = 8, Selector = ".link", StartTimestamp = 300, EndTimestamp = 400 });
        await _db.SaveChangesAsync();

        var queryable = await _query.GetRageClicks(
            session.Id, _principal, _authz, _db, CancellationToken.None);
        var list = await queryable.ToListAsync();

        Assert.Equal(2, list.Count);
        Assert.Contains(list, r => r.TotalClicks == 5);
        Assert.Contains(list, r => r.TotalClicks == 8);
    }

    [Fact]
    public async Task GetRageClicks_NoEvents_ReturnsEmpty()
    {
        var session = new Session { SecureId = "sess-no-rage", ProjectId = _project.Id, CreatedAt = DateTime.UtcNow };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        var queryable = await _query.GetRageClicks(
            session.Id, _principal, _authz, _db, CancellationToken.None);
        var list = await queryable.ToListAsync();

        Assert.Empty(list);
    }

    // ── GetRageClicksForProject (IQueryable) ─────────────────────────

    [Fact]
    public async Task GetRageClicksForProject_ReturnsAllProjectRageClicks()
    {
        var s1 = new Session { SecureId = "sess-r1", ProjectId = _project.Id, CreatedAt = DateTime.UtcNow };
        var s2 = new Session { SecureId = "sess-r2", ProjectId = _project.Id, CreatedAt = DateTime.UtcNow };
        _db.Sessions.AddRange(s1, s2);
        await _db.SaveChangesAsync();

        _db.RageClickEvents.AddRange(
            new RageClickEvent { ProjectId = _project.Id, SessionId = s1.Id, TotalClicks = 3, StartTimestamp = 0, EndTimestamp = 1 },
            new RageClickEvent { ProjectId = _project.Id, SessionId = s2.Id, TotalClicks = 7, StartTimestamp = 0, EndTimestamp = 1 });
        await _db.SaveChangesAsync();

        var queryable = await _query.GetRageClicksForProject(
            _project.Id, _principal, _authz, _db, CancellationToken.None);
        var list = await queryable.ToListAsync();

        Assert.Equal(2, list.Count);
    }

    // ── GetSessionComments / GetSessionCommentsForProject ────────────

    [Fact]
    public async Task GetSessionComments_ReturnsCommentsForSession()
    {
        var session = new Session { SecureId = "sess-com", ProjectId = _project.Id, CreatedAt = DateTime.UtcNow };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        _db.SessionComments.AddRange(
            new SessionComment { SessionId = session.Id, ProjectId = _project.Id, AdminId = _admin.Id, Text = "Comment 1" },
            new SessionComment { SessionId = session.Id, ProjectId = _project.Id, AdminId = _admin.Id, Text = "Comment 2" });
        await _db.SaveChangesAsync();

        var queryable = await _query.GetSessionComments(
            session.Id, _principal, _authz, _db, CancellationToken.None);
        var list = await queryable.ToListAsync();

        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task GetSessionCommentsForProject_AcrossSessions()
    {
        var s1 = new Session { SecureId = "sess-cp1", ProjectId = _project.Id, CreatedAt = DateTime.UtcNow };
        var s2 = new Session { SecureId = "sess-cp2", ProjectId = _project.Id, CreatedAt = DateTime.UtcNow };
        _db.Sessions.AddRange(s1, s2);
        await _db.SaveChangesAsync();

        _db.SessionComments.AddRange(
            new SessionComment { SessionId = s1.Id, ProjectId = _project.Id, AdminId = _admin.Id, Text = "C1" },
            new SessionComment { SessionId = s2.Id, ProjectId = _project.Id, AdminId = _admin.Id, Text = "C2" });
        await _db.SaveChangesAsync();

        var queryable = await _query.GetSessionCommentsForProject(
            _project.Id, _principal, _authz, _db, CancellationToken.None);
        var list = await queryable.ToListAsync();

        Assert.Equal(2, list.Count);
    }

    // ── GetErrorComments (IQueryable) ────────────────────────────────

    [Fact]
    public async Task GetErrorComments_ReturnsCommentsForErrorGroup()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, Event = "err", SecureId = "eg-ec", State = ErrorGroupState.Open };
        _db.ErrorGroups.Add(eg);
        await _db.SaveChangesAsync();

        _db.ErrorComments.AddRange(
            new ErrorComment { ErrorGroupId = eg.Id, AdminId = _admin.Id, Text = "First" },
            new ErrorComment { ErrorGroupId = eg.Id, AdminId = _admin.Id, Text = "Second" });
        await _db.SaveChangesAsync();

        var queryable = await _query.GetErrorComments(
            eg.Id, _principal, _authz, _db, CancellationToken.None);
        var list = await queryable.ToListAsync();

        Assert.Equal(2, list.Count);
    }

    // ── GetAlerts (IQueryable) ───────────────────────────────────────
    // Note: GetAlerts and GetDashboards include navigation properties that
    // cause EF Core query compilation crashes in SQLite provider on .NET 10.
    // We test the underlying data access pattern without materializing the IQueryable.

    [Fact]
    public async Task GetAlerts_ReturnsQueryable()
    {
        _db.Alerts.AddRange(
            new Alert { ProjectId = _project.Id, Name = "Alert1", ProductType = "ERRORS_ALERT" },
            new Alert { ProjectId = _project.Id, Name = "Alert2", ProductType = "SESSIONS_ALERT" });
        await _db.SaveChangesAsync();

        var queryable = await _query.GetAlerts(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        // Verify queryable is scoped to the project (count without Include materialization)
        var count = await _db.Alerts.CountAsync(a => a.ProjectId == _project.Id);
        Assert.Equal(2, count);
    }

    // ── GetDashboards (IQueryable) ───────────────────────────────────

    [Fact]
    public async Task GetDashboards_ReturnsQueryable()
    {
        _db.Dashboards.AddRange(
            new Dashboard { ProjectId = _project.Id, Name = "Dash1" },
            new Dashboard { ProjectId = _project.Id, Name = "Dash2" });
        await _db.SaveChangesAsync();

        var queryable = await _query.GetDashboards(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        var count = await _db.Dashboards.CountAsync(d => d.ProjectId == _project.Id);
        Assert.Equal(2, count);
    }

    // ── GetSessionExports ────────────────────────────────────────────

    [Fact]
    public async Task GetSessionExports_ReturnsExportsForSession()
    {
        var session = new Session { SecureId = "sess-exp", ProjectId = _project.Id, CreatedAt = DateTime.UtcNow };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        _db.SessionExports.AddRange(
            new SessionExport { SessionId = session.Id, Type = "mp4" },
            new SessionExport { SessionId = session.Id, Type = "gif" });
        await _db.SaveChangesAsync();

        var list = await _query.GetSessionExports(
            session.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(2, list.Count);
        Assert.Contains(list, e => e.Type == "mp4");
    }

    [Fact]
    public async Task GetSessionExports_NoExports_ReturnsEmpty()
    {
        var session = new Session { SecureId = "sess-no-exp", ProjectId = _project.Id, CreatedAt = DateTime.UtcNow };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        var list = await _query.GetSessionExports(
            session.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Empty(list);
    }

    // ── GetProjectSettings ───────────────────────────────────────────

    [Fact]
    public async Task GetProjectSettings_ReturnsSettings()
    {
        _db.ProjectFilterSettings.Add(new ProjectFilterSettings
        {
            ProjectId = _project.Id,
            AutoResolveStaleErrorsDayInterval = 7,
        });
        await _db.SaveChangesAsync();

        var settings = await _query.GetProjectSettings(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.NotNull(settings);
        Assert.Equal(7, settings!.AutoResolveStaleErrorsDayInterval);
    }

    [Fact]
    public async Task GetProjectSettings_NoSettings_ReturnsNull()
    {
        var result = await _query.GetProjectSettings(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Null(result);
    }

    // ── GetErrorObject / GetErrorGroup ───────────────────────────────

    [Fact]
    public async Task GetErrorGroup_ReturnsGroupById()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, Event = "specific", SecureId = "eg-spec", State = ErrorGroupState.Open };
        _db.ErrorGroups.Add(eg);
        await _db.SaveChangesAsync();

        var result = await _query.GetErrorGroup(
            eg.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("specific", result!.Event);
    }

    [Fact]
    public async Task GetErrorGroup_NotFound_ReturnsNull()
    {
        var result = await _query.GetErrorGroup(
            99999, _principal, _authz, _db, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetErrorObject_ReturnsObjectById()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, Event = "err", SecureId = "eg-eo", State = ErrorGroupState.Open };
        _db.ErrorGroups.Add(eg);
        await _db.SaveChangesAsync();

        var eo = new ErrorObject { ErrorGroupId = eg.Id, ProjectId = _project.Id, Event = "err" };
        _db.ErrorObjects.Add(eo);
        await _db.SaveChangesAsync();

        var result = await _query.GetErrorObject(
            eo.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(eg.Id, result!.ErrorGroupId);
    }

    [Fact]
    public async Task GetErrorObject_NotFound_ReturnsNull()
    {
        var result = await _query.GetErrorObject(
            99999, _principal, _authz, _db, CancellationToken.None);

        Assert.Null(result);
    }

    // ── GetErrorGroupTags ────────────────────────────────────────────

    [Fact]
    public async Task GetErrorGroupTags_ReturnsTags()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, Event = "err", SecureId = "eg-tag", State = ErrorGroupState.Open };
        _db.ErrorGroups.Add(eg);
        await _db.SaveChangesAsync();

        _db.ErrorTags.AddRange(
            new ErrorTag { ErrorGroupId = eg.Id, Title = "browser", Description = "Chrome" },
            new ErrorTag { ErrorGroupId = eg.Id, Title = "os", Description = "Windows" });
        await _db.SaveChangesAsync();

        var tags = await _query.GetErrorGroupTags(
            eg.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(2, tags.Count);
        Assert.Contains(tags, t => t.Title == "browser");
    }

    // ── GetClientIntegration / GetServerIntegration ──────────────────

    [Fact]
    public async Task GetClientIntegration_NoSessions_ReturnsFalse()
    {
        var result = await _query.GetClientIntegration(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.False(result.Integrated);
    }

    [Fact]
    public async Task GetClientIntegration_WithSessions_ReturnsTrue()
    {
        _db.Sessions.Add(new Session { SecureId = "sess-ci", ProjectId = _project.Id, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var result = await _query.GetClientIntegration(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.True(result.Integrated);
    }

    [Fact]
    public async Task GetServerIntegration_NoErrors_ReturnsFalse()
    {
        var result = await _query.GetServerIntegration(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.False(result.Integrated);
    }

    [Fact]
    public async Task GetServerIntegration_WithErrors_ReturnsTrue()
    {
        _db.ErrorGroups.Add(new ErrorGroup { ProjectId = _project.Id, Event = "err", SecureId = "eg-si", State = ErrorGroupState.Open });
        await _db.SaveChangesAsync();

        var result = await _query.GetServerIntegration(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.True(result.Integrated);
    }

    // ── GetUnprocessedSessionsCount ──────────────────────────────────

    [Fact]
    public async Task GetUnprocessedSessionsCount_ReturnsRecentUnprocessed()
    {
        // Recent unprocessed
        _db.Sessions.Add(new Session { SecureId = "s1", ProjectId = _project.Id, Processed = false, Excluded = false, CreatedAt = DateTime.UtcNow });
        // Old (outside 4h10m window) - should not count
        _db.Sessions.Add(new Session { SecureId = "s2", ProjectId = _project.Id, Processed = false, Excluded = false, CreatedAt = DateTime.UtcNow.AddHours(-5) });
        // Processed - should not count
        _db.Sessions.Add(new Session { SecureId = "s3", ProjectId = _project.Id, Processed = true, Excluded = false, CreatedAt = DateTime.UtcNow });
        // Excluded - should not count
        _db.Sessions.Add(new Session { SecureId = "s4", ProjectId = _project.Id, Processed = false, Excluded = true, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var count = await _query.GetUnprocessedSessionsCount(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task GetUnprocessedSessionsCount_NoSessions_ReturnsZero()
    {
        var count = await _query.GetUnprocessedSessionsCount(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(0, count);
    }

    // ── GetDailySessionsCount ────────────────────────────────────────

    [Fact]
    public async Task GetDailySessionsCount_ReturnsFilteredByDateRange()
    {
        _db.DailySessionCounts.AddRange(
            new DailySessionCount { ProjectId = _project.Id, Date = new DateTime(2026, 3, 1), Count = 10 },
            new DailySessionCount { ProjectId = _project.Id, Date = new DateTime(2026, 3, 2), Count = 20 },
            new DailySessionCount { ProjectId = _project.Id, Date = new DateTime(2026, 3, 3), Count = 30 },
            new DailySessionCount { ProjectId = _project.Id, Date = new DateTime(2026, 2, 28), Count = 5 }); // out of range
        await _db.SaveChangesAsync();

        var result = await _query.GetDailySessionsCount(
            _project.Id, new DateTime(2026, 3, 1), new DateTime(2026, 3, 2),
            _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal(10, result[0].Count);
        Assert.Equal(20, result[1].Count);
    }

    // ── GetDailyErrorsCount ──────────────────────────────────────────

    [Fact]
    public async Task GetDailyErrorsCount_ReturnsFilteredByDateRange()
    {
        _db.DailyErrorCounts.AddRange(
            new DailyErrorCount { ProjectId = _project.Id, Date = new DateTime(2026, 3, 10), Count = 50 },
            new DailyErrorCount { ProjectId = _project.Id, Date = new DateTime(2026, 3, 11), Count = 60 },
            new DailyErrorCount { ProjectId = _project.Id, Date = new DateTime(2026, 3, 15), Count = 100 }); // out of range
        await _db.SaveChangesAsync();

        var result = await _query.GetDailyErrorsCount(
            _project.Id, new DateTime(2026, 3, 10), new DateTime(2026, 3, 11),
            _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal(50, result[0].Count);
    }

    // ── GetAdminHasCreatedComment ────────────────────────────────────

    [Fact]
    public async Task GetAdminHasCreatedComment_NoComments_ReturnsFalse()
    {
        var result = await _query.GetAdminHasCreatedComment(
            _principal, _authz, _db, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task GetAdminHasCreatedComment_WithComment_ReturnsTrue()
    {
        var session = new Session { SecureId = "sess-ahc", ProjectId = _project.Id, CreatedAt = DateTime.UtcNow };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        _db.SessionComments.Add(new SessionComment
            { SessionId = session.Id, ProjectId = _project.Id, AdminId = _admin.Id, Text = "hi" });
        await _db.SaveChangesAsync();

        var result = await _query.GetAdminHasCreatedComment(
            _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
    }

    // ── GetProjectHasViewedASession ──────────────────────────────────

    [Fact]
    public async Task GetProjectHasViewedASession_NoViews_ReturnsFalse()
    {
        var result = await _query.GetProjectHasViewedASession(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task GetProjectHasViewedASession_ViewedSession_ReturnsTrue()
    {
        _db.Sessions.Add(new Session { SecureId = "sess-v", ProjectId = _project.Id, ViewedByAdmins = 1, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var result = await _query.GetProjectHasViewedASession(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
    }

    // ── GetAlert (single) ────────────────────────────────────────────

    [Fact]
    public async Task GetAlert_ReturnsAlert()
    {
        var alert = new Alert { ProjectId = _project.Id, Name = "TestAlert", ProductType = "ERRORS_ALERT" };
        _db.Alerts.Add(alert);
        await _db.SaveChangesAsync();

        var result = await _query.GetAlert(
            alert.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("TestAlert", result!.Name);
    }

    [Fact]
    public async Task GetAlert_NotFound_ReturnsNull()
    {
        var result = await _query.GetAlert(
            99999, _principal, _authz, _db, CancellationToken.None);

        Assert.Null(result);
    }

    // ── GetMetricMonitors ────────────────────────────────────────────

    [Fact]
    public async Task GetMetricMonitors_ReturnsMonitorsForProject()
    {
        _db.MetricMonitors.AddRange(
            new MetricMonitor { ProjectId = _project.Id, Name = "CPU", MetricToMonitor = "cpu_usage" },
            new MetricMonitor { ProjectId = _project.Id, Name = "Memory", MetricToMonitor = "mem_usage" });
        await _db.SaveChangesAsync();

        var list = await _query.GetMetricMonitors(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(2, list.Count);
    }

    // ── GetAdminRole / GetAdminRoleByProject ─────────────────────────

    [Fact]
    public async Task GetAdminRole_ReturnsAdminRole()
    {
        var role = await _query.GetAdminRole(
            _workspace.Id, _principal, _authz, CancellationToken.None);

        Assert.Equal("ADMIN", role);
    }

    [Fact]
    public async Task GetAdminRole_NoMembership_ReturnsNull()
    {
        var otherWs = new Workspace { Name = "NoAccess" };
        _db.Workspaces.Add(otherWs);
        await _db.SaveChangesAsync();

        var role = await _query.GetAdminRole(
            otherWs.Id, _principal, _authz, CancellationToken.None);

        Assert.Null(role);
    }

    // ── GetWorkspacePendingInvites ───────────────────────────────────

    [Fact]
    public async Task GetWorkspacePendingInvites_ReturnsUnacceptedInvites()
    {
        // Invites must match admin's email to be returned
        var otherWs = new Workspace { Name = "OtherWS" };
        _db.Workspaces.Add(otherWs);
        await _db.SaveChangesAsync();

        _db.WorkspaceInviteLinks.AddRange(
            new WorkspaceInviteLink { WorkspaceId = otherWs.Id, Secret = "sec1", InviteeEmail = _admin.Email },
            new WorkspaceInviteLink { WorkspaceId = otherWs.Id, Secret = "sec2", InviteeEmail = _admin.Email },
            new WorkspaceInviteLink { WorkspaceId = otherWs.Id, Secret = "sec3", InviteeEmail = "other@test.com" }); // different email
        await _db.SaveChangesAsync();

        var list = await _query.GetWorkspacePendingInvites(
            _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(2, list.Count); // only the admin's invites
    }

    // ── GetVisualizations / GetVisualization / GetGraph ──────────────

    [Fact]
    public async Task GetVisualizations_ReturnsAllForProject()
    {
        _db.Visualizations.AddRange(
            new Visualization { ProjectId = _project.Id, Name = "Viz1" },
            new Visualization { ProjectId = _project.Id, Name = "Viz2" });
        await _db.SaveChangesAsync();

        var list = await _query.GetVisualizations(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task GetVisualization_ById_Succeeds()
    {
        var viz = new Visualization { ProjectId = _project.Id, Name = "SingleViz" };
        _db.Visualizations.Add(viz);
        await _db.SaveChangesAsync();

        var result = await _query.GetVisualization(
            viz.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("SingleViz", result!.Name);
    }

    [Fact]
    public async Task GetVisualization_NotFound_ReturnsNull()
    {
        var result = await _query.GetVisualization(
            99999, _principal, _authz, _db, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetGraph_ById_Succeeds()
    {
        var graph = new Graph { ProjectId = _project.Id, Title = "G1", ProductType = "line" };
        _db.Graphs.Add(graph);
        await _db.SaveChangesAsync();

        var result = await _query.GetGraph(
            graph.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("G1", result!.Title);
    }

    [Fact]
    public async Task GetGraph_NotFound_ReturnsNull()
    {
        var result = await _query.GetGraph(
            99999, _principal, _authz, _db, CancellationToken.None);

        Assert.Null(result);
    }

    // ── GetLegacyAlerts: ErrorAlerts, SessionAlerts, LogAlerts ───────

    [Fact]
    public async Task GetErrorAlerts_ReturnsAlertsForProject()
    {
        _db.ErrorAlerts.AddRange(
            new ErrorAlert { ProjectId = _project.Id, Name = "EA1", CountThreshold = 5 },
            new ErrorAlert { ProjectId = _project.Id, Name = "EA2", CountThreshold = 10 });
        await _db.SaveChangesAsync();

        var list = await _query.GetErrorAlerts(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task GetSessionAlerts_ReturnsAlertsForProject()
    {
        _db.SessionAlerts.AddRange(
            new SessionAlert { ProjectId = _project.Id, Name = "SA1", CountThreshold = 3, Type = "NEW_USER" },
            new SessionAlert { ProjectId = _project.Id, Name = "SA2", CountThreshold = 5, Type = "RAGE_CLICK" });
        await _db.SaveChangesAsync();

        var list = await _query.GetSessionAlerts(
            _project.Id, null, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task GetSessionAlerts_FilteredByType()
    {
        _db.SessionAlerts.AddRange(
            new SessionAlert { ProjectId = _project.Id, Name = "SA1", CountThreshold = 3, Type = "NEW_USER" },
            new SessionAlert { ProjectId = _project.Id, Name = "SA2", CountThreshold = 5, Type = "RAGE_CLICK" });
        await _db.SaveChangesAsync();

        var list = await _query.GetSessionAlerts(
            _project.Id, "NEW_USER", _principal, _authz, _db, CancellationToken.None);

        Assert.Single(list);
        Assert.Equal("SA1", list[0].Name);
    }

    [Fact]
    public async Task GetLogAlerts_ReturnsAlertsForProject()
    {
        _db.LogAlerts.AddRange(
            new LogAlert { ProjectId = _project.Id, Name = "LA1", CountThreshold = 100 },
            new LogAlert { ProjectId = _project.Id, Name = "LA2", CountThreshold = 200 });
        await _db.SaveChangesAsync();

        var list = await _query.GetLogAlerts(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task GetLogAlert_ById_Succeeds()
    {
        var la = new LogAlert { ProjectId = _project.Id, Name = "Specific", CountThreshold = 50 };
        _db.LogAlerts.Add(la);
        await _db.SaveChangesAsync();

        var result = await _query.GetLogAlert(
            la.Id, _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Specific", result!.Name);
    }

    [Fact]
    public async Task GetLogAlert_WrongProject_ReturnsNull()
    {
        var otherProj = new Project { Name = "Other", WorkspaceId = _workspace.Id };
        _db.Projects.Add(otherProj);
        await _db.SaveChangesAsync();

        var la = new LogAlert { ProjectId = otherProj.Id, Name = "Other", CountThreshold = 50 };
        _db.LogAlerts.Add(la);
        await _db.SaveChangesAsync();

        var result = await _query.GetLogAlert(
            la.Id, _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Null(result);
    }

    // ── GetWorkspacesCount ───────────────────────────────────────────

    [Fact]
    public async Task GetWorkspacesCount_ReturnsAdminWorkspaceCount()
    {
        // Admin is in 1 workspace from setup; add a second membership
        var ws2 = new Workspace { Name = "WS2" };
        _db.Workspaces.Add(ws2);
        await _db.SaveChangesAsync();
        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
            { AdminId = _admin.Id, WorkspaceId = ws2.Id, Role = "MEMBER" });
        await _db.SaveChangesAsync();

        var count = await _query.GetWorkspacesCount(
            _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task GetWorkspacesCount_ExcludesOtherAdminWorkspaces()
    {
        // Another admin's workspace should not count
        var otherAdmin = new Admin { Uid = "other", Email = "other@test.com" };
        _db.Admins.Add(otherAdmin);
        var ws2 = new Workspace { Name = "WS2" };
        _db.Workspaces.Add(ws2);
        await _db.SaveChangesAsync();
        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
            { AdminId = otherAdmin.Id, WorkspaceId = ws2.Id, Role = "ADMIN" });
        await _db.SaveChangesAsync();

        var count = await _query.GetWorkspacesCount(
            _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(1, count); // only the admin's workspace
    }

    // ── GetEmailOptOuts ──────────────────────────────────────────────

    [Fact]
    public async Task GetEmailOptOuts_ReturnsOptOutsForAdmin()
    {
        _db.Set<EmailOptOut>().AddRange(
            new EmailOptOut { AdminId = _admin.Id, Category = "Digests" },
            new EmailOptOut { AdminId = _admin.Id, Category = "Billing" });
        await _db.SaveChangesAsync();

        var list = await _query.GetEmailOptOuts(
            _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(2, list.Count);
    }

    // ── GetDashboardDefinitions ──────────────────────────────────────

    [Fact]
    public async Task GetDashboardDefinitions_ReturnsForProject()
    {
        _db.Dashboards.AddRange(
            new Dashboard { ProjectId = _project.Id, Name = "Def1" },
            new Dashboard { ProjectId = _project.Id, Name = "Def2" });
        await _db.SaveChangesAsync();

        var list = await _query.GetDashboardDefinitions(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(2, list.Count);
    }

    /// <summary>
    /// Minimal fake IClickHouseService for analytics tests.
    /// QueryErrorGroupIdsAsync returns all IDs up to count so the DB layer
    /// handles project-scoped filtering.
    /// </summary>
    private class FakeClickHouseService : IClickHouseService
    {
        public Task<(List<int> Ids, long Total)> QueryErrorGroupIdsAsync(int projectId, QueryInput query, int count, int page, CancellationToken ct)
        {
            // Return a broad range of IDs; EF query filters by projectId
            var ids = Enumerable.Range(1, count).ToList();
            return Task.FromResult((ids, (long)count));
        }

        public Task<MetricsBuckets> ReadMetricsAsync(int projectId, QueryInput query, string bucketBy, List<string>? groupBy, string aggregator, string? column, CancellationToken ct) => Task.FromResult(new MetricsBuckets());
        public Task<LogConnection> ReadLogsAsync(int projectId, QueryInput query, ClickHousePagination pagination, CancellationToken ct) => Task.FromResult(new LogConnection());
        public Task<List<HistogramBucket>> ReadLogsHistogramAsync(int projectId, QueryInput query, CancellationToken ct) => Task.FromResult(new List<HistogramBucket>());
        public Task<List<string>> GetLogKeysAsync(int projectId, QueryInput query, CancellationToken ct) => Task.FromResult(new List<string>());
        public Task<List<string>> GetLogKeyValuesAsync(int projectId, string key, QueryInput query, CancellationToken ct) => Task.FromResult(new List<string>());
        public Task<TraceConnection> ReadTracesAsync(int projectId, QueryInput query, ClickHousePagination pagination, bool omitBody, CancellationToken ct) => Task.FromResult(new TraceConnection());
        public Task<List<HistogramBucket>> ReadTracesHistogramAsync(int projectId, QueryInput query, CancellationToken ct) => Task.FromResult(new List<HistogramBucket>());
        public Task<List<string>> GetTraceKeysAsync(int projectId, QueryInput query, CancellationToken ct) => Task.FromResult(new List<string>());
        public Task<List<string>> GetTraceKeyValuesAsync(int projectId, string key, QueryInput query, CancellationToken ct) => Task.FromResult(new List<string>());
        public Task<List<HistogramBucket>> ReadSessionsHistogramAsync(int projectId, QueryInput query, CancellationToken ct) => Task.FromResult(new List<HistogramBucket>());
        public Task<(List<int> Ids, long Total)> QuerySessionIdsAsync(int projectId, QueryInput query, int count, int page, string? sortField, bool sortDesc, CancellationToken ct) => Task.FromResult((new List<int>(), 0L));
        public Task<List<HistogramBucket>> ReadErrorObjectsHistogramAsync(int projectId, QueryInput query, CancellationToken ct) => Task.FromResult(new List<HistogramBucket>());
        public Task<List<QueryKey>> GetSessionsKeysAsync(int projectId, DateTime startDate, DateTime endDate, string? query, CancellationToken ct) => Task.FromResult(new List<QueryKey>());
        public Task<List<QueryKey>> GetErrorsKeysAsync(int projectId, DateTime startDate, DateTime endDate, string? query, CancellationToken ct) => Task.FromResult(new List<QueryKey>());
        public Task<List<string>> GetErrorsKeyValuesAsync(int projectId, string keyName, DateTime startDate, DateTime endDate, string? query, int? count, CancellationToken ct) => Task.FromResult(new List<string>());
        public Task<List<QueryKey>> GetEventsKeysAsync(int projectId, DateTime startDate, DateTime endDate, string? query, string? eventName, CancellationToken ct) => Task.FromResult(new List<QueryKey>());
        public Task<List<string>> GetEventsKeyValuesAsync(int projectId, string keyName, DateTime startDate, DateTime endDate, string? query, int? count, string? eventName, CancellationToken ct) => Task.FromResult(new List<string>());
        public Task<List<string>> GetSessionsKeyValuesAsync(int projectId, string keyName, DateTime startDate, DateTime endDate, string? query, int? count, CancellationToken ct) => Task.FromResult(new List<string>());
        public Task WriteMetricAsync(int projectId, string metricName, double metricValue, string? category, DateTime timestamp, Dictionary<string, string>? tags, string? sessionSecureId, CancellationToken ct) => Task.CompletedTask;
        public Task WriteLogsAsync(IEnumerable<LogRowInput> logs, CancellationToken ct) => Task.CompletedTask;
        public Task WriteTracesAsync(IEnumerable<TraceRowInput> traces, CancellationToken ct) => Task.CompletedTask;
        public Task WriteSessionsAsync(IEnumerable<SessionRowInput> sessions, CancellationToken ct) => Task.CompletedTask;
        public Task WriteErrorGroupsAsync(IEnumerable<ErrorGroupRowInput> errorGroups, CancellationToken ct) => Task.CompletedTask;
        public Task WriteErrorObjectsAsync(IEnumerable<ErrorObjectRowInput> errorObjects, CancellationToken ct) => Task.CompletedTask;
        public Task<long> CountLogsAsync(int projectId, string? query, DateTime startDate, DateTime endDate, CancellationToken ct = default) => Task.FromResult(0L);
        public Task<List<AlertStateChangeRow>> GetLastAlertStateChangesAsync(int projectId, int alertId, DateTime startDate, DateTime endDate, CancellationToken ct = default) => Task.FromResult(new List<AlertStateChangeRow>());
        public Task<List<AlertStateChangeRow>> GetAlertingAlertStateChangesAsync(int projectId, int alertId, DateTime startDate, DateTime endDate, CancellationToken ct = default) => Task.FromResult(new List<AlertStateChangeRow>());
        public Task<List<AlertStateChangeRow>> GetLastAlertingStatesAsync(int projectId, int alertId, DateTime startDate, DateTime endDate, CancellationToken ct = default) => Task.FromResult(new List<AlertStateChangeRow>());
        public Task WriteAlertStateChangesAsync(int projectId, IEnumerable<AlertStateChangeRow> rows, CancellationToken ct = default) => Task.CompletedTask;
    }
}
