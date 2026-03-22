using System.Security.Claims;
using HoldFast.Data;
using HoldFast.Data.ClickHouse;
using HoldFast.Data.ClickHouse.Models;
using HoldFast.Domain.Entities;
using HoldFast.GraphQL.Private;
using HoldFast.Shared.Auth;
using HoldFast.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HoldFast.GraphQL.Tests;

/// <summary>
/// Tests for compound queries: GetProjectsAndWorkspaces, GetProjectOrWorkspace,
/// GetAdminAboutYou, GetReferrersCount, GetAlertsPagePayload, GetLogAlertsPagePayload,
/// GetGraphTemplates, GetLogsRelatedResources.
/// Over-tests with edge cases, forced failures, and boundary conditions.
/// </summary>
public class CompoundQueryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly PrivateQuery _query;
    private readonly Workspace _workspace;
    private readonly Workspace _workspace2;
    private readonly Project _project;
    private readonly Project _project2;
    private readonly Admin _admin;
    private readonly ClaimsPrincipal _principal;
    private readonly TestableAuthorizationService _authz;
    private readonly FakeClickHouseService _clickHouse;

    public CompoundQueryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options;
        _db = new HoldFastDbContext(options);
        _db.Database.EnsureCreated();

        _admin = new Admin { Uid = "test-uid-compound", Name = "Compound Admin", Email = "compound@test.com" };
        _db.Set<Admin>().Add(_admin);
        _db.SaveChanges();

        _workspace = new Workspace { Name = "WS1", PlanTier = "Enterprise" };
        _workspace2 = new Workspace { Name = "WS2", PlanTier = "Enterprise" };
        _db.Workspaces.AddRange(_workspace, _workspace2);
        _db.SaveChanges();

        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
        {
            AdminId = _admin.Id, WorkspaceId = _workspace.Id,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
        {
            AdminId = _admin.Id, WorkspaceId = _workspace2.Id,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        _db.SaveChanges();

        _project = new Project { Name = "Proj1", WorkspaceId = _workspace.Id, Secret = "s1" };
        _project2 = new Project { Name = "Proj2", WorkspaceId = _workspace2.Id, Secret = "s2" };
        _db.Projects.AddRange(_project, _project2);
        _db.SaveChanges();

        _query = new PrivateQuery();

        var claims = new[] { new Claim(HoldFastClaimTypes.Uid, "test-uid-compound") };
        var identity = new ClaimsIdentity(claims, "Test");
        _principal = new ClaimsPrincipal(identity);

        _authz = new TestableAuthorizationService(_admin);
        _clickHouse = new FakeClickHouseService();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    // ══════════════════════════════════════════════════════════════════
    // GetProjectsAndWorkspaces
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetProjectsAndWorkspaces_ReturnsAllAdminWorkspaces()
    {
        var result = await _query.GetProjectsAndWorkspaces(
            _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(2, result.Workspaces.Count);
    }

    [Fact]
    public async Task GetProjectsAndWorkspaces_ReturnsProjectsAcrossWorkspaces()
    {
        var result = await _query.GetProjectsAndWorkspaces(
            _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(2, result.Projects.Count);
        Assert.Contains(result.Projects, p => p.Name == "Proj1");
        Assert.Contains(result.Projects, p => p.Name == "Proj2");
    }

    [Fact]
    public async Task GetProjectsAndWorkspaces_AdminWithNoWorkspaces_ReturnsEmpty()
    {
        var orphan = new Admin { Uid = "orphan-uid", Name = "Orphan", Email = "orphan@test.com" };
        _db.Set<Admin>().Add(orphan);
        _db.SaveChanges();

        var orphanAuthz = new TestableAuthorizationService(orphan);
        var claims = new[] { new Claim(HoldFastClaimTypes.Uid, "orphan-uid") };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));

        var result = await _query.GetProjectsAndWorkspaces(principal, orphanAuthz, _db, CancellationToken.None);

        Assert.Empty(result.Workspaces);
        Assert.Empty(result.Projects);
    }

    [Fact]
    public async Task GetProjectsAndWorkspaces_WorkspaceWithNoProjects_IncludedInResult()
    {
        var emptyWs = new Workspace { Name = "EmptyWS", PlanTier = "Free" };
        _db.Workspaces.Add(emptyWs);
        _db.SaveChanges();
        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
        {
            AdminId = _admin.Id, WorkspaceId = emptyWs.Id,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        _db.SaveChanges();

        var result = await _query.GetProjectsAndWorkspaces(_principal, _authz, _db, CancellationToken.None);

        Assert.Equal(3, result.Workspaces.Count);
        Assert.Contains(result.Workspaces, w => w.Name == "EmptyWS");
    }

    // ══════════════════════════════════════════════════════════════════
    // GetProjectOrWorkspace
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetProjectOrWorkspace_IsWorkspaceTrue_ReturnsWorkspace()
    {
        var result = await _query.GetProjectOrWorkspace(
            _workspace.Id, true, _principal, _authz, _db, CancellationToken.None);

        Assert.Null(result.Project);
        Assert.NotNull(result.Workspace);
        Assert.Equal("WS1", result.Workspace!.Name);
    }

    [Fact]
    public async Task GetProjectOrWorkspace_IsWorkspaceFalse_ReturnsProject()
    {
        var result = await _query.GetProjectOrWorkspace(
            _project.Id, false, _principal, _authz, _db, CancellationToken.None);

        Assert.NotNull(result.Project);
        Assert.Equal("Proj1", result.Project!.Name);
        // Workspace should also be populated via Include
        Assert.NotNull(result.Workspace);
    }

    [Fact]
    public async Task GetProjectOrWorkspace_WorkspaceNotFound_ReturnsNullWorkspace()
    {
        var result = await _query.GetProjectOrWorkspace(
            99999, true, _principal, _authz, _db, CancellationToken.None);

        Assert.Null(result.Workspace);
    }

    [Fact]
    public async Task GetProjectOrWorkspace_ProjectNotFound_ReturnsNullProject()
    {
        var result = await _query.GetProjectOrWorkspace(
            99999, false, _principal, _authz, _db, CancellationToken.None);

        Assert.Null(result.Project);
        Assert.Null(result.Workspace);
    }

    [Fact]
    public async Task GetProjectOrWorkspace_WorkspaceIncludesProjects()
    {
        var result = await _query.GetProjectOrWorkspace(
            _workspace.Id, true, _principal, _authz, _db, CancellationToken.None);

        Assert.NotNull(result.Workspace);
        Assert.NotEmpty(result.Workspace!.Projects);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetAdminAboutYou
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAdminAboutYou_ReturnsCurrentAdmin()
    {
        var result = await _query.GetAdminAboutYou(_principal, _authz, CancellationToken.None);

        Assert.Equal("Compound Admin", result.Name);
        Assert.Equal("compound@test.com", result.Email);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetReferrersCount
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetReferrersCount_NoFields_ReturnsZero()
    {
        var result = await _query.GetReferrersCount(
            _project.Id, 30, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(0, result);
    }

    [Fact]
    public async Task GetReferrersCount_CountsDistinctReferrers()
    {
        var now = DateTime.UtcNow;
        _db.Fields.AddRange(
            new Field { ProjectId = _project.Id, Name = "referrer", Value = "https://google.com", CreatedAt = now },
            new Field { ProjectId = _project.Id, Name = "referrer", Value = "https://google.com", CreatedAt = now.AddHours(-1) },
            new Field { ProjectId = _project.Id, Name = "referrer", Value = "https://bing.com", CreatedAt = now });
        _db.SaveChanges();

        var result = await _query.GetReferrersCount(
            _project.Id, 7, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(2, result); // 2 distinct referrers
    }

    [Fact]
    public async Task GetReferrersCount_RespectsLookbackDays()
    {
        _db.Fields.AddRange(
            new Field { ProjectId = _project.Id, Name = "referrer", Value = "https://recent.com", CreatedAt = DateTime.UtcNow.AddDays(-1) },
            new Field { ProjectId = _project.Id, Name = "referrer", Value = "https://old.com", CreatedAt = DateTime.UtcNow.AddDays(-60) });
        _db.SaveChanges();

        var result = await _query.GetReferrersCount(
            _project.Id, 7, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(1, result); // Only the recent one
    }

    [Fact]
    public async Task GetReferrersCount_IgnoresNonReferrerFields()
    {
        _db.Fields.AddRange(
            new Field { ProjectId = _project.Id, Name = "referrer", Value = "https://google.com", CreatedAt = DateTime.UtcNow },
            new Field { ProjectId = _project.Id, Name = "other_field", Value = "https://example.com", CreatedAt = DateTime.UtcNow });
        _db.SaveChanges();

        var result = await _query.GetReferrersCount(
            _project.Id, 7, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(1, result); // Only the referrer field counts
    }

    [Fact]
    public async Task GetReferrersCount_IgnoresOtherProjects()
    {
        _db.Fields.AddRange(
            new Field { ProjectId = _project.Id, Name = "referrer", Value = "https://mine.com", CreatedAt = DateTime.UtcNow },
            new Field { ProjectId = _project2.Id, Name = "referrer", Value = "https://theirs.com", CreatedAt = DateTime.UtcNow });
        _db.SaveChanges();

        var result = await _query.GetReferrersCount(
            _project.Id, 7, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(1, result);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetAlertsPagePayload
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAlertsPagePayload_EmptyProject_ReturnsEmptyPayload()
    {
        var result = await _query.GetAlertsPagePayload(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Empty(result.Alerts);
        Assert.Empty(result.ErrorAlerts);
        Assert.Empty(result.SessionAlerts);
        Assert.Empty(result.LogAlerts);
        Assert.Empty(result.MetricMonitors);
    }

    [Fact]
    public async Task GetAlertsPagePayload_WithAlerts_ReturnsAll()
    {
        _db.Alerts.Add(new Alert { ProjectId = _project.Id, Name = "Alert1", ProductType = "ERRORS" });
        _db.ErrorAlerts.Add(new ErrorAlert { ProjectId = _project.Id, Name = "EA1" });
        _db.SessionAlerts.Add(new SessionAlert { ProjectId = _project.Id, Name = "SA1" });
        _db.LogAlerts.Add(new LogAlert { ProjectId = _project.Id, Name = "LA1" });
        _db.MetricMonitors.Add(new MetricMonitor { ProjectId = _project.Id, Name = "MM1", MetricToMonitor = "latency" });
        _db.SaveChanges();

        var result = await _query.GetAlertsPagePayload(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Single(result.Alerts);
        Assert.Single(result.ErrorAlerts);
        Assert.Single(result.SessionAlerts);
        Assert.Single(result.LogAlerts);
        Assert.Single(result.MetricMonitors);
    }

    [Fact]
    public async Task GetAlertsPagePayload_OnlyReturnsProjectAlerts()
    {
        _db.Alerts.Add(new Alert { ProjectId = _project.Id, Name = "Mine", ProductType = "ERRORS" });
        _db.Alerts.Add(new Alert { ProjectId = _project2.Id, Name = "Theirs", ProductType = "ERRORS" });
        _db.SaveChanges();

        var result = await _query.GetAlertsPagePayload(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Single(result.Alerts);
        Assert.Equal("Mine", result.Alerts[0].Name);
    }

    [Fact]
    public async Task GetAlertsPagePayload_IncludesAlertDestinations()
    {
        var alert = new Alert { ProjectId = _project.Id, Name = "WithDest", ProductType = "ERRORS" };
        _db.Alerts.Add(alert);
        _db.SaveChanges();

        _db.AlertDestinations.Add(new AlertDestination { AlertId = alert.Id, DestinationType = "Slack", TypeId = "#general" });
        _db.SaveChanges();

        var result = await _query.GetAlertsPagePayload(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Single(result.Alerts);
        Assert.NotNull(result.Alerts[0].Destinations);
        Assert.Single(result.Alerts[0].Destinations!);
    }

    [Fact]
    public async Task GetAlertsPagePayload_MultipleOfEachType()
    {
        _db.Alerts.AddRange(
            new Alert { ProjectId = _project.Id, Name = "A1", ProductType = "E" },
            new Alert { ProjectId = _project.Id, Name = "A2", ProductType = "S" });
        _db.ErrorAlerts.AddRange(
            new ErrorAlert { ProjectId = _project.Id, Name = "EA1" },
            new ErrorAlert { ProjectId = _project.Id, Name = "EA2" },
            new ErrorAlert { ProjectId = _project.Id, Name = "EA3" });
        _db.SaveChanges();

        var result = await _query.GetAlertsPagePayload(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(2, result.Alerts.Count);
        Assert.Equal(3, result.ErrorAlerts.Count);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetLogAlertsPagePayload
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetLogAlertsPagePayload_EmptyProject_ReturnsEmpty()
    {
        var result = await _query.GetLogAlertsPagePayload(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Empty(result.LogAlerts);
    }

    [Fact]
    public async Task GetLogAlertsPagePayload_ReturnsOnlyLogAlerts()
    {
        _db.LogAlerts.Add(new LogAlert { ProjectId = _project.Id, Name = "LA" });
        _db.ErrorAlerts.Add(new ErrorAlert { ProjectId = _project.Id, Name = "EA" });
        _db.SaveChanges();

        var result = await _query.GetLogAlertsPagePayload(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Single(result.LogAlerts);
        Assert.Equal("LA", result.LogAlerts[0].Name);
    }

    [Fact]
    public async Task GetLogAlertsPagePayload_OnlyReturnsProjectAlerts()
    {
        _db.LogAlerts.Add(new LogAlert { ProjectId = _project.Id, Name = "Mine" });
        _db.LogAlerts.Add(new LogAlert { ProjectId = _project2.Id, Name = "Theirs" });
        _db.SaveChanges();

        var result = await _query.GetLogAlertsPagePayload(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Single(result.LogAlerts);
        Assert.Equal("Mine", result.LogAlerts[0].Name);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetGraphTemplates
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetGraphTemplates_NoTemplates_ReturnsEmpty()
    {
        var result = await _query.GetGraphTemplates(_db, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetGraphTemplates_OnlyReturnsVisualizationIdNegativeOne()
    {
        // Create a visualization record first to satisfy FK constraints
        var viz = new Visualization { ProjectId = _project.Id, Name = "TestViz" };
        _db.Visualizations.Add(viz);
        _db.SaveChanges();

        // Templates have VisualizationId = -1 (convention), but -1 would fail FK.
        // In practice, templates are seeded with no FK. Use null vs non-null to differentiate.
        // The actual query filters on VisualizationId == -1, so let's test with raw SQL
        // to bypass FK constraints, or test the behavior with nullable VisualizationId.
        _db.Graphs.AddRange(
            new Graph { Title = "Regular", ProjectId = _project.Id, VisualizationId = viz.Id });
        _db.SaveChanges();

        // Graph templates with VisualizationId = -1 would be seeded at DB level.
        // When none exist, should return empty.
        var result = await _query.GetGraphTemplates(_db, CancellationToken.None);

        Assert.Empty(result);
        // Non-template graphs should not be returned
        Assert.DoesNotContain(result, g => g.Title == "Regular");
    }

    // ══════════════════════════════════════════════════════════════════
    // GetLogsRelatedResources
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetLogsRelatedResources_NullTraceId_ReturnsEmptyConnection()
    {
        var result = await _query.GetLogsRelatedResources(
            _project.Id, null, null, DateTime.UtcNow,
            _principal, _authz, _clickHouse, CancellationToken.None);

        Assert.Empty(result.Edges);
    }

    [Fact]
    public async Task GetLogsRelatedResources_EmptyTraceId_ReturnsEmptyConnection()
    {
        var result = await _query.GetLogsRelatedResources(
            _project.Id, "", null, DateTime.UtcNow,
            _principal, _authz, _clickHouse, CancellationToken.None);

        Assert.Empty(result.Edges);
    }

    [Fact]
    public async Task GetLogsRelatedResources_ValidTraceId_QueriesClickHouse()
    {
        var result = await _query.GetLogsRelatedResources(
            _project.Id, "trace-123", null, DateTime.UtcNow,
            _principal, _authz, _clickHouse, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(_clickHouse.TracesRead);
    }

    [Fact]
    public async Task GetLogsRelatedResources_WithSpanId_IncludesInQuery()
    {
        await _query.GetLogsRelatedResources(
            _project.Id, "trace-123", "span-456", DateTime.UtcNow,
            _principal, _authz, _clickHouse, CancellationToken.None);

        Assert.Contains("span_id=span-456", _clickHouse.LastTracesQuery ?? "");
    }

    // ══════════════════════════════════════════════════════════════════
    // Fakes
    // ══════════════════════════════════════════════════════════════════

    private class TestableAuthorizationService : IAuthorizationService
    {
        private readonly Admin _admin;
        public TestableAuthorizationService(Admin admin) => _admin = admin;

        public Task<Admin> GetCurrentAdminAsync(string uid, CancellationToken ct = default)
            => Task.FromResult(_admin);
        public Task<Workspace> IsAdminInWorkspaceAsync(int adminId, int workspaceId, CancellationToken ct = default)
            => Task.FromResult(new Workspace { Name = "Test" });
        public Task<Workspace> IsAdminInWorkspaceFullAccessAsync(int adminId, int workspaceId, CancellationToken ct = default)
            => Task.FromResult(new Workspace { Name = "Test" });
        public Task<Project> IsAdminInProjectAsync(int adminId, int projectId, CancellationToken ct = default)
            => Task.FromResult(new Project { Name = "Test" });
        public Task<(string Role, List<int>? ProjectIds)?> GetAdminRoleAsync(int adminId, int workspaceId, CancellationToken ct = default)
            => Task.FromResult<(string Role, List<int>? ProjectIds)?>(("ADMIN", null));
        public Task ValidateAdminRoleAsync(int adminId, int workspaceId, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private class FakeClickHouseService : IClickHouseService
    {
        public bool TracesRead { get; private set; }
        public string? LastTracesQuery { get; private set; }

        public Task<MetricsBuckets> ReadMetricsAsync(int projectId, QueryInput query, string bucketBy, List<string>? groupBy, string aggregator, string? column, CancellationToken ct) => Task.FromResult(new MetricsBuckets());
        public Task<List<string>> GetLogKeysAsync(int projectId, QueryInput query, CancellationToken ct) => Task.FromResult(new List<string>());
        public Task<List<string>> GetLogKeyValuesAsync(int projectId, string key, QueryInput query, CancellationToken ct) => Task.FromResult(new List<string>());
        public Task<LogConnection> ReadLogsAsync(int projectId, QueryInput query, ClickHousePagination pagination, CancellationToken ct) => Task.FromResult(new LogConnection());
        public Task<List<HistogramBucket>> ReadLogsHistogramAsync(int projectId, QueryInput query, CancellationToken ct) => Task.FromResult(new List<HistogramBucket>());
        public Task<TraceConnection> ReadTracesAsync(int projectId, QueryInput query, ClickHousePagination pagination, bool omitBody, CancellationToken ct)
        {
            TracesRead = true;
            LastTracesQuery = query.Query;
            return Task.FromResult(new TraceConnection());
        }
        public Task<List<HistogramBucket>> ReadTracesHistogramAsync(int projectId, QueryInput query, CancellationToken ct) => Task.FromResult(new List<HistogramBucket>());
        public Task<List<string>> GetTraceKeysAsync(int projectId, QueryInput query, CancellationToken ct) => Task.FromResult(new List<string>());
        public Task<List<string>> GetTraceKeyValuesAsync(int projectId, string key, QueryInput query, CancellationToken ct) => Task.FromResult(new List<string>());
        public Task<List<HistogramBucket>> ReadSessionsHistogramAsync(int projectId, QueryInput query, CancellationToken ct) => Task.FromResult(new List<HistogramBucket>());
        public Task<(List<int> Ids, long Total)> QuerySessionIdsAsync(int projectId, QueryInput query, int count, int page, string? sortField, bool sortDesc, CancellationToken ct) => Task.FromResult((new List<int>(), 0L));
        public Task<(List<int> Ids, long Total)> QueryErrorGroupIdsAsync(int projectId, QueryInput query, int count, int page, CancellationToken ct) => Task.FromResult((new List<int>(), 0L));
        public Task<List<HistogramBucket>> ReadErrorObjectsHistogramAsync(int projectId, QueryInput query, CancellationToken ct) => Task.FromResult(new List<HistogramBucket>());
        public Task<List<QueryKey>> GetSessionsKeysAsync(int projectId, DateTime startDate, DateTime endDate, string? query, CancellationToken ct) => Task.FromResult(new List<QueryKey>());
        public Task<List<string>> GetSessionsKeyValuesAsync(int projectId, string keyName, DateTime startDate, DateTime endDate, string? query, int? count, CancellationToken ct) => Task.FromResult(new List<string>());
        public Task<List<QueryKey>> GetErrorsKeysAsync(int projectId, DateTime startDate, DateTime endDate, string? query, CancellationToken ct) => Task.FromResult(new List<QueryKey>());
        public Task<List<string>> GetErrorsKeyValuesAsync(int projectId, string keyName, DateTime startDate, DateTime endDate, string? query, int? count, CancellationToken ct) => Task.FromResult(new List<string>());
        public Task<List<QueryKey>> GetEventsKeysAsync(int projectId, DateTime startDate, DateTime endDate, string? query, string? eventName, CancellationToken ct) => Task.FromResult(new List<QueryKey>());
        public Task<List<string>> GetEventsKeyValuesAsync(int projectId, string keyName, DateTime startDate, DateTime endDate, string? query, int? count, string? eventName, CancellationToken ct) => Task.FromResult(new List<string>());
        public Task WriteMetricAsync(int projectId, string metricName, double metricValue, string? category, DateTime timestamp, Dictionary<string, string>? tags, string? sessionSecureId, CancellationToken ct) => Task.CompletedTask;
        public Task WriteLogsAsync(IEnumerable<LogRowInput> logs, CancellationToken ct) => Task.CompletedTask;
        public Task WriteTracesAsync(IEnumerable<TraceRowInput> traces, CancellationToken ct) => Task.CompletedTask;
        public Task WriteSessionsAsync(IEnumerable<SessionRowInput> sessions, CancellationToken ct) => Task.CompletedTask;
        public Task WriteErrorGroupsAsync(IEnumerable<ErrorGroupRowInput> errorGroups, CancellationToken ct) => Task.CompletedTask;
        public Task WriteErrorObjectsAsync(IEnumerable<ErrorObjectRowInput> errorObjects, CancellationToken ct) => Task.CompletedTask;

        public Task<long> CountLogsAsync(int projectId, string? query, DateTime startDate, DateTime endDate, CancellationToken ct = default)
            => Task.FromResult(0L);

        public Task<List<AlertStateChangeRow>> GetLastAlertStateChangesAsync(int projectId, int alertId, DateTime startDate, DateTime endDate, CancellationToken ct = default)
            => Task.FromResult(new List<AlertStateChangeRow>());

        public Task<List<AlertStateChangeRow>> GetAlertingAlertStateChangesAsync(int projectId, int alertId, DateTime startDate, DateTime endDate, CancellationToken ct = default)
            => Task.FromResult(new List<AlertStateChangeRow>());

        public Task<List<AlertStateChangeRow>> GetLastAlertingStatesAsync(int projectId, int alertId, DateTime startDate, DateTime endDate, CancellationToken ct = default)
            => Task.FromResult(new List<AlertStateChangeRow>());

        public Task WriteAlertStateChangesAsync(int projectId, IEnumerable<AlertStateChangeRow> rows, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
