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
/// Tests for PrivateQuery methods that use only the DB (no ClickHouse).
/// Covers workspace, project, error group, session, alert, visualization,
/// integration status, and analytics queries.
/// </summary>
public class PrivateQueryDbTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly AuthorizationService _authz;
    private readonly PrivateQuery _query;
    private readonly ClaimsPrincipal _principal;
    private readonly Admin _admin;
    private readonly Workspace _workspace;
    private readonly Project _project;

    public PrivateQueryDbTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options;
        _db = new HoldFastDbContext(options);
        _db.Database.EnsureCreated();
        _query = new PrivateQuery();

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
        _connection.Close();
        _connection.Dispose();
    }

    // ── Workspace Queries ───────────────────────────────────────────

    [Fact]
    public async Task GetWorkspace_ReturnsWorkspace()
    {
        var ws = await _query.GetWorkspace(_workspace.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.NotNull(ws);
        Assert.Equal("WS", ws!.Name);
    }

    [Fact]
    public async Task GetWorkspace_IncludesProjects()
    {
        var ws = await _query.GetWorkspace(_workspace.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.NotNull(ws);
        Assert.NotEmpty(ws!.Projects);
    }

    [Fact]
    public async Task GetWorkspace_Nonexistent_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _query.GetWorkspace(99999, _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task GetWorkspaceForProject_ReturnsParentWorkspace()
    {
        var ws = await _query.GetWorkspaceForProject(_project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.NotNull(ws);
        Assert.Equal(_workspace.Id, ws!.Id);
    }

    [Fact]
    public async Task GetWorkspaceForInviteLink_ValidSecret()
    {
        var invite = new WorkspaceInviteLink
        {
            WorkspaceId = _workspace.Id,
            InviteeEmail = "test@test.com",
            Secret = "test-secret-123",
            ExpirationDate = DateTime.UtcNow.AddDays(7),
        };
        _db.WorkspaceInviteLinks.Add(invite);
        await _db.SaveChangesAsync();

        var ws = await _query.GetWorkspaceForInviteLink("test-secret-123", _principal, _authz, _db, CancellationToken.None);
        Assert.NotNull(ws);
        Assert.Equal(_workspace.Id, ws!.Id);
    }

    [Fact]
    public async Task GetWorkspaceForInviteLink_InvalidSecret_ReturnsNull()
    {
        var ws = await _query.GetWorkspaceForInviteLink("bad-secret", _principal, _authz, _db, CancellationToken.None);
        Assert.Null(ws);
    }

    [Fact]
    public async Task GetWorkspaceInviteLinks_ReturnsLinks()
    {
        _db.WorkspaceInviteLinks.Add(new WorkspaceInviteLink
            { WorkspaceId = _workspace.Id, InviteeEmail = "a@test.com", Secret = "s1" });
        _db.WorkspaceInviteLinks.Add(new WorkspaceInviteLink
            { WorkspaceId = _workspace.Id, InviteeEmail = "b@test.com", Secret = "s2" });
        await _db.SaveChangesAsync();

        var links = await _query.GetWorkspaceInviteLinks(_workspace.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.Equal(2, links.Count);
    }

    [Fact]
    public async Task GetWorkspaceInviteLinks_Empty()
    {
        var links = await _query.GetWorkspaceInviteLinks(_workspace.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.Empty(links);
    }

    [Fact]
    public async Task GetWorkspaceAdmins_ReturnsAdmins()
    {
        var admins = await _query.GetWorkspaceAdmins(_workspace.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.Single(admins);
        Assert.Equal(_workspace.Id.ToString(), admins[0].WorkspaceId);
        Assert.Equal("ADMIN", admins[0].Role);
    }

    // ── Project Queries ─────────────────────────────────────────────

    [Fact]
    public async Task GetProject_ReturnsProject()
    {
        var proj = await _query.GetProject(_project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.NotNull(proj);
        Assert.Equal("Proj", proj!.Name);
    }

    [Fact]
    public async Task GetProjectSettings_ReturnsDefaults_WhenNone()
    {
        // Resolver always returns AllProjectSettings (with defaults) if the project exists.
        var settings = await _query.GetProjectSettings(_project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.NotNull(settings);
        Assert.Equal(_project.Id, settings!.Id);
        Assert.False(settings.FilterSessionsWithoutError);
    }

    [Fact]
    public async Task GetProjectSettings_ReturnsSettings_WhenExists()
    {
        _db.ProjectFilterSettings.Add(new ProjectFilterSettings
        {
            ProjectId = _project.Id,
            FilterSessionsWithoutError = true,
        });
        await _db.SaveChangesAsync();

        var settings = await _query.GetProjectSettings(_project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.NotNull(settings);
        Assert.True(settings!.FilterSessionsWithoutError);
    }

    // ── Error Group Queries ─────────────────────────────────────────

    [Fact]
    public async Task GetErrorGroup_ReturnsErrorGroup()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, Event = "err", Type = "Error" };
        _db.ErrorGroups.Add(eg);
        await _db.SaveChangesAsync();

        var result = await _query.GetErrorGroup(eg.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal("err", result!.Event);
    }

    [Fact]
    public async Task GetErrorGroup_Nonexistent_ReturnsNull()
    {
        var result = await _query.GetErrorGroup(99999, _principal, _authz, _db, CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetErrorGroupTags_ReturnsEmpty_WhenNone()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, Event = "err", Type = "Error" };
        _db.ErrorGroups.Add(eg);
        await _db.SaveChangesAsync();

        var tags = await _query.GetErrorGroupTags(eg.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.Empty(tags);
    }

    [Fact]
    public async Task GetErrorGroupTags_ReturnsTags()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, Event = "err", Type = "Error" };
        _db.ErrorGroups.Add(eg);
        await _db.SaveChangesAsync();

        _db.ErrorTags.Add(new ErrorTag { ErrorGroupId = eg.Id, Title = "tag1" });
        _db.ErrorTags.Add(new ErrorTag { ErrorGroupId = eg.Id, Title = "tag2" });
        await _db.SaveChangesAsync();

        var tags = await _query.GetErrorGroupTags(eg.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.Equal(2, tags.Count);
    }

    // ── Session Queries ─────────────────────────────────────────────

    [Fact]
    public async Task GetSession_BySecureId()
    {
        var session = new Session
        {
            ProjectId = _project.Id, SecureId = "sess-get",
            Fingerprint = "fp1", City = "", State = "", Country = "",
        };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        var result = await _query.GetSession("sess-get", _principal, _authz, _db, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal("sess-get", result!.SecureId);
    }

    [Fact]
    public async Task GetSession_Nonexistent_ReturnsNull()
    {
        var result = await _query.GetSession("nope", _principal, _authz, _db, CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetSessionIntervals_ReturnsOrdered()
    {
        var session = new Session
        {
            ProjectId = _project.Id, SecureId = "sess-int",
            Fingerprint = "fp1", City = "", State = "", Country = "",
        };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        _db.SessionIntervals.Add(new SessionInterval
            { SessionId = session.Id, StartTime = 2000, Duration = 5000 });
        _db.SessionIntervals.Add(new SessionInterval
            { SessionId = session.Id, StartTime = 1000, Duration = 5000 });
        await _db.SaveChangesAsync();

        var intervals = await _query.GetSessionIntervals(session.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.Equal(2, intervals.Count);
        Assert.Equal(1000, intervals[0].StartTime);
        Assert.Equal(2000, intervals[1].StartTime);
    }

    [Fact]
    public async Task GetEventChunks_ReturnsOrdered()
    {
        var session = new Session
        {
            ProjectId = _project.Id, SecureId = "sess-chunk",
            Fingerprint = "fp1", City = "", State = "", Country = "",
        };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        _db.EventChunks.Add(new EventChunk { SessionId = session.Id, ChunkIndex = 2, Timestamp = 3000 });
        _db.EventChunks.Add(new EventChunk { SessionId = session.Id, ChunkIndex = 0, Timestamp = 1000 });
        _db.EventChunks.Add(new EventChunk { SessionId = session.Id, ChunkIndex = 1, Timestamp = 2000 });
        await _db.SaveChangesAsync();

        var chunks = await _query.GetEventChunks(session.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.Equal(3, chunks.Count);
        Assert.Equal(0, chunks[0].ChunkIndex);
        Assert.Equal(1, chunks[1].ChunkIndex);
        Assert.Equal(2, chunks[2].ChunkIndex);
    }

    // ── Integration Status ──────────────────────────────────────────

    [Fact]
    public async Task GetClientIntegration_NoSessions_ReturnsFalse()
    {
        var result = await _query.GetClientIntegration(_project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.False(result.Integrated);
    }

    [Fact]
    public async Task GetClientIntegration_WithSessions_ReturnsTrue()
    {
        _db.Sessions.Add(new Session
        {
            ProjectId = _project.Id, SecureId = "s1",
            Fingerprint = "fp", City = "", State = "", Country = "",
        });
        await _db.SaveChangesAsync();

        var result = await _query.GetClientIntegration(_project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.True(result.Integrated);
    }

    [Fact]
    public async Task GetServerIntegration_NoErrors_ReturnsFalse()
    {
        var result = await _query.GetServerIntegration(_project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.False(result.Integrated);
    }

    [Fact]
    public async Task GetServerIntegration_WithErrors_ReturnsTrue()
    {
        _db.ErrorGroups.Add(new ErrorGroup { ProjectId = _project.Id, Event = "err", Type = "Error" });
        await _db.SaveChangesAsync();

        var result = await _query.GetServerIntegration(_project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.True(result.Integrated);
    }

    // ── Admin Check Flags ───────────────────────────────────────────

    [Fact]
    public async Task GetAdminHasCreatedComment_NoComments_ReturnsFalse()
    {
        var result = await _query.GetAdminHasCreatedComment(_principal, _authz, _db, CancellationToken.None);
        Assert.False(result);
    }

    [Fact]
    public async Task GetAdminHasCreatedComment_WithComment_ReturnsTrue()
    {
        var session = new Session
        {
            ProjectId = _project.Id, SecureId = "s-cmt",
            Fingerprint = "fp", City = "", State = "", Country = "",
        };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        _db.SessionComments.Add(new SessionComment
            { SessionId = session.Id, AdminId = _admin.Id, Text = "hi" });
        await _db.SaveChangesAsync();

        var result = await _query.GetAdminHasCreatedComment(_principal, _authz, _db, CancellationToken.None);
        Assert.True(result);
    }

    [Fact]
    public async Task GetProjectHasViewedASession_NoViews_ReturnsFalse()
    {
        var result = await _query.GetProjectHasViewedASession(_project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.False(result);
    }

    [Fact]
    public async Task GetProjectHasViewedASession_WithViews_ReturnsTrue()
    {
        _db.Sessions.Add(new Session
        {
            ProjectId = _project.Id, SecureId = "s-view",
            Fingerprint = "fp", City = "", State = "", Country = "",
            ViewedByAdmins = 3,
        });
        await _db.SaveChangesAsync();

        var result = await _query.GetProjectHasViewedASession(_project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.True(result);
    }

    // ── Legacy Alert Queries ────────────────────────────────────────

    [Fact]
    public async Task GetErrorAlerts_ReturnsAlerts()
    {
        _db.ErrorAlerts.Add(new ErrorAlert { ProjectId = _project.Id, Name = "Alert1" });
        _db.ErrorAlerts.Add(new ErrorAlert { ProjectId = _project.Id, Name = "Alert2" });
        await _db.SaveChangesAsync();

        var alerts = await _query.GetErrorAlerts(_project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.Equal(2, alerts.Count);
    }

    [Fact]
    public async Task GetErrorAlerts_Empty()
    {
        var alerts = await _query.GetErrorAlerts(_project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.Empty(alerts);
    }

    [Fact]
    public async Task GetSessionAlerts_ReturnsAll()
    {
        _db.SessionAlerts.Add(new SessionAlert { ProjectId = _project.Id, Name = "A1", Type = "NEW_SESSION" });
        _db.SessionAlerts.Add(new SessionAlert { ProjectId = _project.Id, Name = "A2", Type = "RAGE_CLICK" });
        await _db.SaveChangesAsync();

        var alerts = await _query.GetSessionAlerts(_project.Id, null, _principal, _authz, _db, CancellationToken.None);
        Assert.Equal(2, alerts.Count);
    }

    [Fact]
    public async Task GetSessionAlerts_FilterByType()
    {
        _db.SessionAlerts.Add(new SessionAlert { ProjectId = _project.Id, Name = "A1", Type = "NEW_SESSION" });
        _db.SessionAlerts.Add(new SessionAlert { ProjectId = _project.Id, Name = "A2", Type = "RAGE_CLICK" });
        await _db.SaveChangesAsync();

        var alerts = await _query.GetSessionAlerts(_project.Id, "NEW_SESSION", _principal, _authz, _db, CancellationToken.None);
        Assert.Single(alerts);
        Assert.Equal("NEW_SESSION", alerts[0].Type);
    }

    [Fact]
    public async Task GetLogAlerts_ReturnsAlerts()
    {
        _db.LogAlerts.Add(new LogAlert { ProjectId = _project.Id, Name = "Log1" });
        await _db.SaveChangesAsync();

        var alerts = await _query.GetLogAlerts(_project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.Single(alerts);
    }

    [Fact]
    public async Task GetLogAlert_ById()
    {
        var alert = new LogAlert { ProjectId = _project.Id, Name = "SpecificLog" };
        _db.LogAlerts.Add(alert);
        await _db.SaveChangesAsync();

        var result = await _query.GetLogAlert(alert.Id, _project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal("SpecificLog", result!.Name);
    }

    [Fact]
    public async Task GetLogAlert_Nonexistent_ReturnsNull()
    {
        var result = await _query.GetLogAlert(99999, _project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetLogAlert_WrongProject_Throws()
    {
        var alert = new LogAlert { ProjectId = _project.Id, Name = "Log" };
        _db.LogAlerts.Add(alert);
        await _db.SaveChangesAsync();

        // Query with wrong project ID throws auth error
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _query.GetLogAlert(alert.Id, 99999, _principal, _authz, _db, CancellationToken.None));
    }

    // ── Metric Monitors ─────────────────────────────────────────────

    [Fact]
    public async Task GetMetricMonitors_ReturnsMonitors()
    {
        _db.MetricMonitors.Add(new MetricMonitor
        {
            ProjectId = _project.Id, Name = "CPU",
            MetricToMonitor = "cpu", Aggregator = "avg",
        });
        await _db.SaveChangesAsync();

        var monitors = await _query.GetMetricMonitors(_project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.Single(monitors);
    }

    [Fact]
    public async Task GetMetricMonitors_Empty()
    {
        var monitors = await _query.GetMetricMonitors(_project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.Empty(monitors);
    }

    // ── Visualization & Graph Queries ───────────────────────────────

    [Fact]
    public async Task GetVisualizations_ReturnsAll()
    {
        _db.Visualizations.Add(new Visualization { ProjectId = _project.Id, Name = "V1" });
        _db.Visualizations.Add(new Visualization { ProjectId = _project.Id, Name = "V2" });
        await _db.SaveChangesAsync();

        var vizs = await _query.GetVisualizations(_project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.Equal(2, vizs.Count);
    }

    [Fact]
    public async Task GetVisualizations_IncludesGraphs()
    {
        var viz = new Visualization { ProjectId = _project.Id, Name = "WithGraphs" };
        _db.Visualizations.Add(viz);
        await _db.SaveChangesAsync();

        _db.Graphs.Add(new Graph { VisualizationId = viz.Id, ProjectId = _project.Id, Title = "G1" });
        await _db.SaveChangesAsync();

        var vizs = await _query.GetVisualizations(_project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.Single(vizs);
        Assert.NotEmpty(vizs[0].Graphs);
    }

    [Fact]
    public async Task GetVisualization_ById()
    {
        var viz = new Visualization { ProjectId = _project.Id, Name = "Single" };
        _db.Visualizations.Add(viz);
        await _db.SaveChangesAsync();

        var result = await _query.GetVisualization(viz.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal("Single", result!.Name);
    }

    [Fact]
    public async Task GetVisualization_Nonexistent_ReturnsNull()
    {
        var result = await _query.GetVisualization(99999, _principal, _authz, _db, CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetGraph_ById()
    {
        var viz = new Visualization { ProjectId = _project.Id, Name = "V" };
        _db.Visualizations.Add(viz);
        await _db.SaveChangesAsync();

        var graph = new Graph { VisualizationId = viz.Id, ProjectId = _project.Id, Title = "MyGraph" };
        _db.Graphs.Add(graph);
        await _db.SaveChangesAsync();

        var result = await _query.GetGraph(graph.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal("MyGraph", result!.Title);
    }

    [Fact]
    public async Task GetGraph_Nonexistent_ReturnsNull()
    {
        var result = await _query.GetGraph(99999, _principal, _authz, _db, CancellationToken.None);
        Assert.Null(result);
    }

    // ── Analytics ────────────────────────────────────────────────────

    [Fact]
    public async Task GetUnprocessedSessionsCount_NoSessions_ReturnsZero()
    {
        var count = await _query.GetUnprocessedSessionsCount(_project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task GetUnprocessedSessionsCount_CountsUnprocessed()
    {
        var now = DateTime.UtcNow;
        // Recent unprocessed
        _db.Sessions.Add(new Session
        {
            ProjectId = _project.Id, SecureId = "s-unproc1",
            Fingerprint = "fp", City = "", State = "", Country = "",
            Processed = false, Excluded = false, CreatedAt = now,
        });
        // Recent processed (should not count)
        _db.Sessions.Add(new Session
        {
            ProjectId = _project.Id, SecureId = "s-proc",
            Fingerprint = "fp", City = "", State = "", Country = "",
            Processed = true, Excluded = false, CreatedAt = now,
        });
        // Recent excluded (should not count)
        _db.Sessions.Add(new Session
        {
            ProjectId = _project.Id, SecureId = "s-excl",
            Fingerprint = "fp", City = "", State = "", Country = "",
            Processed = false, Excluded = true, CreatedAt = now,
        });
        await _db.SaveChangesAsync();

        var count = await _query.GetUnprocessedSessionsCount(_project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task GetLiveUsersCount_DistinctIdentifiers()
    {
        var now = DateTime.UtcNow;
        // Two sessions with same identifier
        _db.Sessions.Add(new Session
        {
            ProjectId = _project.Id, SecureId = "s-live1",
            Fingerprint = "fp1", City = "", State = "", Country = "",
            Identifier = "user@test.com", Processed = false, CreatedAt = now,
        });
        _db.Sessions.Add(new Session
        {
            ProjectId = _project.Id, SecureId = "s-live2",
            Fingerprint = "fp2", City = "", State = "", Country = "",
            Identifier = "user@test.com", Processed = false, CreatedAt = now,
        });
        // One session with different identifier
        _db.Sessions.Add(new Session
        {
            ProjectId = _project.Id, SecureId = "s-live3",
            Fingerprint = "fp3", City = "", State = "", Country = "",
            Identifier = "other@test.com", Processed = false, CreatedAt = now,
        });
        await _db.SaveChangesAsync();

        var count = await _query.GetLiveUsersCount(_project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.Equal(2, count); // 2 distinct identifiers
    }

    [Fact]
    public async Task GetDailySessionsCount_ReturnsOrdered()
    {
        _db.DailySessionCounts.Add(new DailySessionCount
            { ProjectId = _project.Id, Date = new DateTime(2026, 3, 20), Count = 10 });
        _db.DailySessionCounts.Add(new DailySessionCount
            { ProjectId = _project.Id, Date = new DateTime(2026, 3, 18), Count = 5 });
        _db.DailySessionCounts.Add(new DailySessionCount
            { ProjectId = _project.Id, Date = new DateTime(2026, 3, 19), Count = 8 });
        await _db.SaveChangesAsync();

        var counts = await _query.GetDailySessionsCount(
            _project.Id, new DateTime(2026, 3, 18), new DateTime(2026, 3, 20),
            _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(3, counts.Count);
        Assert.Equal(new DateTime(2026, 3, 18), counts[0].Date);
        Assert.Equal(new DateTime(2026, 3, 19), counts[1].Date);
        Assert.Equal(new DateTime(2026, 3, 20), counts[2].Date);
    }

    [Fact]
    public async Task GetDailySessionsCount_DateRangeFilter()
    {
        _db.DailySessionCounts.Add(new DailySessionCount
            { ProjectId = _project.Id, Date = new DateTime(2026, 3, 15), Count = 1 });
        _db.DailySessionCounts.Add(new DailySessionCount
            { ProjectId = _project.Id, Date = new DateTime(2026, 3, 20), Count = 5 });
        _db.DailySessionCounts.Add(new DailySessionCount
            { ProjectId = _project.Id, Date = new DateTime(2026, 3, 25), Count = 10 });
        await _db.SaveChangesAsync();

        var counts = await _query.GetDailySessionsCount(
            _project.Id, new DateTime(2026, 3, 18), new DateTime(2026, 3, 22),
            _principal, _authz, _db, CancellationToken.None);

        Assert.Single(counts);
        Assert.Equal(5, counts[0].Count);
    }

    [Fact]
    public async Task GetDailyErrorsCount_ReturnsOrdered()
    {
        _db.DailyErrorCounts.Add(new DailyErrorCount
            { ProjectId = _project.Id, Date = new DateTime(2026, 3, 20), Count = 3 });
        _db.DailyErrorCounts.Add(new DailyErrorCount
            { ProjectId = _project.Id, Date = new DateTime(2026, 3, 19), Count = 7 });
        await _db.SaveChangesAsync();

        var counts = await _query.GetDailyErrorsCount(
            _project.Id, new DateTime(2026, 3, 19), new DateTime(2026, 3, 20),
            _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(2, counts.Count);
        Assert.True(counts[0].Date < counts[1].Date);
    }

    // ── Workspace Admins By Project ─────────────────────────────────

    [Fact]
    public async Task GetWorkspaceAdminsByProjectId_ReturnsAdmins()
    {
        var admins = await _query.GetWorkspaceAdminsByProjectId(
            _project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.Single(admins);
        Assert.Equal(_admin.Id, admins[0].Admin.Id);
    }

    [Fact]
    public async Task GetWorkspaceAdminsByProjectId_MultipleAdmins()
    {
        var admin2 = new Admin { Uid = "admin-2", Email = "admin2@test.com" };
        _db.Admins.Add(admin2);
        await _db.SaveChangesAsync();

        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
            { AdminId = admin2.Id, WorkspaceId = _workspace.Id, Role = "MEMBER" });
        await _db.SaveChangesAsync();

        var admins = await _query.GetWorkspaceAdminsByProjectId(
            _project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.Equal(2, admins.Count);
    }

    [Fact]
    public async Task GetWorkspaceAdminsByProjectId_NonexistentProject_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _query.GetWorkspaceAdminsByProjectId(
                99999, _principal, _authz, _db, CancellationToken.None));
    }

    // ── Alert Detail ────────────────────────────────────────────────

    [Fact]
    public async Task GetAlert_ReturnsAlert()
    {
        var alert = new Alert { ProjectId = _project.Id, Name = "TestAlert", ProductType = "sessions" };
        _db.Alerts.Add(alert);
        await _db.SaveChangesAsync();

        var result = await _query.GetAlert(alert.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal("TestAlert", result!.Name);
    }

    [Fact]
    public async Task GetAlert_Nonexistent_ReturnsNull()
    {
        var result = await _query.GetAlert(99999, _principal, _authz, _db, CancellationToken.None);
        Assert.Null(result);
    }

    // ── Admin Role Queries ──────────────────────────────────────────

    [Fact]
    public async Task GetAdminRole_ReturnsRole()
    {
        var result = await _query.GetAdminRole(_workspace.Id, _principal, _authz, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal("ADMIN", result.Role);
    }

    [Fact]
    public async Task GetAdminRole_NonMember_ReturnsNull()
    {
        var ws2 = new Workspace { Name = "Other" };
        _db.Workspaces.Add(ws2);
        await _db.SaveChangesAsync();

        var result = await _query.GetAdminRole(ws2.Id, _principal, _authz, CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAdminRoleByProject_ReturnsRole()
    {
        var role = await _query.GetAdminRoleByProject(_project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.NotNull(role);
        Assert.Equal("ADMIN", role!.Role);
    }

    [Fact]
    public async Task GetAdminRoleByProject_NonexistentProject_ReturnsNull()
    {
        var role = await _query.GetAdminRoleByProject(99999, _principal, _authz, _db, CancellationToken.None);
        Assert.Null(role);
    }

    // ── Workspace Pending Invites ───────────────────────────────────

    [Fact]
    public async Task GetWorkspacePendingInvites_ReturnsActive()
    {
        _db.WorkspaceInviteLinks.Add(new WorkspaceInviteLink
        {
            WorkspaceId = _workspace.Id, InviteeEmail = "admin@test.com",
            Secret = "active1", ExpirationDate = DateTime.UtcNow.AddDays(7),
        });
        // Expired invite (should not be returned)
        _db.WorkspaceInviteLinks.Add(new WorkspaceInviteLink
        {
            WorkspaceId = _workspace.Id, InviteeEmail = "admin@test.com",
            Secret = "expired1", ExpirationDate = DateTime.UtcNow.AddDays(-1),
        });
        await _db.SaveChangesAsync();

        var invites = await _query.GetWorkspacePendingInvites(_principal, _authz, _db, CancellationToken.None);
        Assert.Single(invites);
        Assert.Equal("active1", invites[0].Secret);
    }

    [Fact]
    public async Task GetWorkspacePendingInvites_NullExpiration_IsActive()
    {
        _db.WorkspaceInviteLinks.Add(new WorkspaceInviteLink
        {
            WorkspaceId = _workspace.Id, InviteeEmail = "admin@test.com",
            Secret = "no-expiry", ExpirationDate = null,
        });
        await _db.SaveChangesAsync();

        var invites = await _query.GetWorkspacePendingInvites(_principal, _authz, _db, CancellationToken.None);
        Assert.Single(invites);
    }

    [Fact]
    public async Task GetWorkspacePendingInvites_Empty()
    {
        var invites = await _query.GetWorkspacePendingInvites(_principal, _authz, _db, CancellationToken.None);
        Assert.Empty(invites);
    }

    // ── Joinable Workspaces ─────────────────────────────────────────

    [Fact]
    public async Task GetJoinableWorkspaces_ReturnsDistinct()
    {
        var ws2 = new Workspace { Name = "Joinable" };
        _db.Workspaces.Add(ws2);
        await _db.SaveChangesAsync();

        // Two invites to same workspace — should return one workspace
        _db.WorkspaceInviteLinks.Add(new WorkspaceInviteLink
        {
            WorkspaceId = ws2.Id, InviteeEmail = "admin@test.com",
            Secret = "j1", ExpirationDate = DateTime.UtcNow.AddDays(7),
        });
        _db.WorkspaceInviteLinks.Add(new WorkspaceInviteLink
        {
            WorkspaceId = ws2.Id, InviteeEmail = "admin@test.com",
            Secret = "j2", ExpirationDate = DateTime.UtcNow.AddDays(3),
        });
        await _db.SaveChangesAsync();

        var workspaces = await _query.GetJoinableWorkspaces(_principal, _authz, _db, CancellationToken.None);
        Assert.Single(workspaces);
        Assert.Equal("Joinable", workspaces[0].Name);
    }

    [Fact]
    public async Task GetJoinableWorkspaces_ExcludesExpired()
    {
        var ws2 = new Workspace { Name = "Expired" };
        _db.Workspaces.Add(ws2);
        await _db.SaveChangesAsync();

        _db.WorkspaceInviteLinks.Add(new WorkspaceInviteLink
        {
            WorkspaceId = ws2.Id, InviteeEmail = "admin@test.com",
            Secret = "exp", ExpirationDate = DateTime.UtcNow.AddDays(-1),
        });
        await _db.SaveChangesAsync();

        var workspaces = await _query.GetJoinableWorkspaces(_principal, _authz, _db, CancellationToken.None);
        Assert.Empty(workspaces);
    }

    // ── IsSessionPending ────────────────────────────────────────────

    [Fact]
    public async Task IsSessionPending_Unprocessed_ReturnsTrue()
    {
        var session = new Session
        {
            ProjectId = _project.Id, SecureId = "s-pending",
            Fingerprint = "fp", City = "", State = "", Country = "",
            Processed = false,
        };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        var pending = await _query.IsSessionPending(session.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.True(pending);
    }

    [Fact]
    public async Task IsSessionPending_Processed_ReturnsFalse()
    {
        var session = new Session
        {
            ProjectId = _project.Id, SecureId = "s-done",
            Fingerprint = "fp", City = "", State = "", Country = "",
            Processed = true,
        };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        var pending = await _query.IsSessionPending(session.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.False(pending);
    }

    [Fact]
    public async Task IsSessionPending_Nonexistent_ReturnsFalse()
    {
        var pending = await _query.IsSessionPending(99999, _principal, _authz, _db, CancellationToken.None);
        Assert.False(pending);
    }

    // ── GetErrorInstance ─────────────────────────────────────────────

    [Fact]
    public async Task GetErrorInstance_SpecificObject()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, Event = "err", Type = "Error" };
        _db.ErrorGroups.Add(eg);
        await _db.SaveChangesAsync();

        var eo = new ErrorObject { ErrorGroupId = eg.Id, Event = "err" };
        _db.ErrorObjects.Add(eo);
        await _db.SaveChangesAsync();

        var result = await _query.GetErrorInstance(eg.Id, eo.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal(eo.Id, result!.Id);
    }

    [Fact]
    public async Task GetErrorInstance_NullObjectId_ReturnsLatest()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, Event = "err", Type = "Error" };
        _db.ErrorGroups.Add(eg);
        await _db.SaveChangesAsync();

        var eo1 = new ErrorObject { ErrorGroupId = eg.Id, Event = "old", CreatedAt = DateTime.UtcNow.AddHours(-1) };
        var eo2 = new ErrorObject { ErrorGroupId = eg.Id, Event = "new", CreatedAt = DateTime.UtcNow };
        _db.ErrorObjects.AddRange(eo1, eo2);
        await _db.SaveChangesAsync();

        var result = await _query.GetErrorInstance(eg.Id, null, _principal, _authz, _db, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal("new", result!.Event);
    }

    [Fact]
    public async Task GetErrorInstance_NoObjects_ReturnsNull()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, Event = "err", Type = "Error" };
        _db.ErrorGroups.Add(eg);
        await _db.SaveChangesAsync();

        var result = await _query.GetErrorInstance(eg.Id, null, _principal, _authz, _db, CancellationToken.None);
        Assert.Null(result);
    }

    // ── GetErrorObjectForLog ─────────────────────────────────────────

    [Fact]
    public async Task GetErrorObjectForLog_ReturnsObject()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, Event = "err", Type = "Error" };
        _db.ErrorGroups.Add(eg);
        await _db.SaveChangesAsync();

        var eo = new ErrorObject { ErrorGroupId = eg.Id, Event = "err" };
        _db.ErrorObjects.Add(eo);
        await _db.SaveChangesAsync();

        var result = await _query.GetErrorObjectForLog(eo.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal(eo.Id, result!.Id);
    }

    [Fact]
    public async Task GetErrorObjectForLog_Nonexistent_ReturnsNull()
    {
        var result = await _query.GetErrorObjectForLog(99999, _principal, _authz, _db, CancellationToken.None);
        Assert.Null(result);
    }

    // ── GetWorkspacesCount ──────────────────────────────────────────

    [Fact]
    public async Task GetWorkspacesCount_ReturnsCount()
    {
        var count = await _query.GetWorkspacesCount(_principal, _authz, _db, CancellationToken.None);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task GetWorkspacesCount_MultipleWorkspaces()
    {
        var ws2 = new Workspace { Name = "WS2" };
        _db.Workspaces.Add(ws2);
        await _db.SaveChangesAsync();

        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
            { AdminId = _admin.Id, WorkspaceId = ws2.Id, Role = "MEMBER" });
        await _db.SaveChangesAsync();

        var count = await _query.GetWorkspacesCount(_principal, _authz, _db, CancellationToken.None);
        Assert.Equal(2, count);
    }
}
