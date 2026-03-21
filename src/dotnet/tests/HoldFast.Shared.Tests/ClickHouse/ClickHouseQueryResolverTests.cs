using System.Security.Claims;
using HoldFast.Data;
using HoldFast.Data.ClickHouse;
using HoldFast.Data.ClickHouse.Models;
using HoldFast.Domain.Entities;
using HoldFast.GraphQL.Private;
using HoldFast.Shared.Auth;
using HotChocolate;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HoldFast.Shared.Tests.ClickHouse;

/// <summary>
/// Tests for PrivateQuery ClickHouse-backed resolvers (logs, traces, metrics, search).
/// Uses a stub IClickHouseService to verify auth and delegation without a live ClickHouse.
/// </summary>
public class ClickHouseQueryResolverTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly AuthorizationService _authz;
    private readonly PrivateQuery _query;
    private readonly StubClickHouseService _clickHouse;

    public ClickHouseQueryResolverTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options;
        _db = new HoldFastDbContext(options);
        _db.Database.EnsureCreated();
        _authz = new AuthorizationService(_db);
        _query = new PrivateQuery();
        _clickHouse = new StubClickHouseService();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    private static ClaimsPrincipal MakePrincipal(string uid) =>
        new(new ClaimsIdentity(new[]
        {
            new Claim(HoldFastClaimTypes.Uid, uid),
            new Claim(HoldFastClaimTypes.Email, $"{uid}@test.com"),
        }, "Test"));

    private async Task<(Admin admin, Workspace workspace, Project project)> SeedFullStack(string uid = "admin-uid")
    {
        var admin = new Admin { Uid = uid, Email = $"{uid}@test.com" };
        _db.Admins.Add(admin);
        var workspace = new Workspace { Name = "Test WS" };
        _db.Workspaces.Add(workspace);
        await _db.SaveChangesAsync();

        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
        {
            AdminId = admin.Id, WorkspaceId = workspace.Id, Role = "ADMIN",
        });

        var project = new Project { Name = "Test Project", WorkspaceId = workspace.Id };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync();

        return (admin, workspace, project);
    }

    // ── GetLogs ────────────────────────────────────────────────────

    [Fact]
    public async Task GetLogs_WithAccess_DelegatesToClickHouse()
    {
        var (_, _, project) = await SeedFullStack();

        var result = await _query.GetLogs(
            project.Id, "error", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow,
            null, null, null, "DESC", 50,
            MakePrincipal("admin-uid"), _authz, _clickHouse, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("ReadLogsAsync", _clickHouse.LastCalledMethod);
        Assert.Equal(project.Id, _clickHouse.LastProjectId);
    }

    [Fact]
    public async Task GetLogs_NoAccess_ThrowsGraphQLException()
    {
        var (_, _, project) = await SeedFullStack();

        // Create an unauthenticated admin with no project access
        var outsider = new Admin { Uid = "outsider", Email = "outsider@test.com" };
        _db.Admins.Add(outsider);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _query.GetLogs(project.Id, "", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow,
                null, null, null, "DESC", 50,
                MakePrincipal("outsider"), _authz, _clickHouse, CancellationToken.None));

        Assert.Null(_clickHouse.LastCalledMethod); // ClickHouse should NOT have been called
    }

    [Fact]
    public async Task GetLogs_Unauthenticated_ThrowsGraphQLException()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _query.GetLogs(1, "", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow,
                null, null, null, "DESC", 50,
                anonymous, _authz, _clickHouse, CancellationToken.None));
    }

    // ── GetLogsHistogram ───────────────────────────────────────────

    [Fact]
    public async Task GetLogsHistogram_WithAccess_ReturnsHistogram()
    {
        var (_, _, project) = await SeedFullStack();

        var result = await _query.GetLogsHistogram(
            project.Id, "", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow,
            MakePrincipal("admin-uid"), _authz, _clickHouse, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("ReadLogsHistogramAsync", _clickHouse.LastCalledMethod);
    }

    // ── GetLogKeys ─────────────────────────────────────────────────

    [Fact]
    public async Task GetLogKeys_WithAccess_ReturnsKeys()
    {
        var (_, _, project) = await SeedFullStack();

        var result = await _query.GetLogKeys(
            project.Id, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, null,
            MakePrincipal("admin-uid"), _authz, _clickHouse, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("GetLogKeysAsync", _clickHouse.LastCalledMethod);
    }

    // ── GetLogKeyValues ────────────────────────────────────────────

    [Fact]
    public async Task GetLogKeyValues_WithAccess_ReturnsValues()
    {
        var (_, _, project) = await SeedFullStack();

        var result = await _query.GetLogKeyValues(
            project.Id, "service_name", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow,
            MakePrincipal("admin-uid"), _authz, _clickHouse, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("GetLogKeyValuesAsync", _clickHouse.LastCalledMethod);
    }

    // ── GetTraces ──────────────────────────────────────────────────

    [Fact]
    public async Task GetTraces_WithAccess_DelegatesToClickHouse()
    {
        var (_, _, project) = await SeedFullStack();

        var result = await _query.GetTraces(
            project.Id, "", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow,
            null, null, null, "DESC", 50,
            MakePrincipal("admin-uid"), _authz, _clickHouse, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("ReadTracesAsync", _clickHouse.LastCalledMethod);
    }

    [Fact]
    public async Task GetTraces_NoAccess_ThrowsGraphQLException()
    {
        var (_, _, project) = await SeedFullStack();
        var outsider = new Admin { Uid = "outsider2", Email = "outsider2@test.com" };
        _db.Admins.Add(outsider);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _query.GetTraces(project.Id, "", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow,
                null, null, null, "DESC", 50,
                MakePrincipal("outsider2"), _authz, _clickHouse, CancellationToken.None));
    }

    // ── GetTracesHistogram ─────────────────────────────────────────

    [Fact]
    public async Task GetTracesHistogram_WithAccess_ReturnsHistogram()
    {
        var (_, _, project) = await SeedFullStack();

        var result = await _query.GetTracesHistogram(
            project.Id, "", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow,
            MakePrincipal("admin-uid"), _authz, _clickHouse, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("ReadTracesHistogramAsync", _clickHouse.LastCalledMethod);
    }

    // ── GetTraceKeys / GetTraceKeyValues ───────────────────────────

    [Fact]
    public async Task GetTraceKeys_WithAccess_ReturnsKeys()
    {
        var (_, _, project) = await SeedFullStack();

        var result = await _query.GetTraceKeys(
            project.Id, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, null,
            MakePrincipal("admin-uid"), _authz, _clickHouse, CancellationToken.None);

        Assert.Equal("GetTraceKeysAsync", _clickHouse.LastCalledMethod);
    }

    [Fact]
    public async Task GetTraceKeyValues_WithAccess_ReturnsValues()
    {
        var (_, _, project) = await SeedFullStack();

        var result = await _query.GetTraceKeyValues(
            project.Id, "http.method", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow,
            MakePrincipal("admin-uid"), _authz, _clickHouse, CancellationToken.None);

        Assert.Equal("GetTraceKeyValuesAsync", _clickHouse.LastCalledMethod);
    }

    // ── GetMetrics ─────────────────────────────────────────────────

    [Fact]
    public async Task GetMetrics_WithAccess_DelegatesToClickHouse()
    {
        var (_, _, project) = await SeedFullStack();

        var result = await _query.GetMetrics(
            project.Id, "", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow,
            "timestamp", null, "COUNT", null,
            MakePrincipal("admin-uid"), _authz, _clickHouse, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("ReadMetricsAsync", _clickHouse.LastCalledMethod);
    }

    [Fact]
    public async Task GetMetrics_WithGroupBy_PassesGroupBy()
    {
        var (_, _, project) = await SeedFullStack();

        await _query.GetMetrics(
            project.Id, "", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow,
            "timestamp", ["service_name"], "P95", "Duration",
            MakePrincipal("admin-uid"), _authz, _clickHouse, CancellationToken.None);

        Assert.Equal("ReadMetricsAsync", _clickHouse.LastCalledMethod);
        Assert.Equal("P95", _clickHouse.LastAggregator);
    }

    // ── SearchErrorGroups ──────────────────────────────────────────

    [Fact]
    public async Task SearchErrorGroups_WithAccess_ReturnsResult()
    {
        var (_, _, project) = await SeedFullStack();

        var result = await _query.SearchErrorGroups(
            project.Id, "", DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, 25, 0,
            MakePrincipal("admin-uid"), _authz, _clickHouse, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(0, result.TotalCount); // Stub returns empty
        Assert.Equal("QueryErrorGroupIdsAsync", _clickHouse.LastCalledMethod);
    }

    [Fact]
    public async Task SearchErrorGroups_NoAccess_ThrowsGraphQLException()
    {
        var (_, _, project) = await SeedFullStack();
        var outsider = new Admin { Uid = "outsider3", Email = "outsider3@test.com" };
        _db.Admins.Add(outsider);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _query.SearchErrorGroups(project.Id, "", DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, 25, 0,
                MakePrincipal("outsider3"), _authz, _clickHouse, CancellationToken.None));
    }

    // ── GetErrorObjectsHistogram ───────────────────────────────────

    [Fact]
    public async Task GetErrorObjectsHistogram_WithAccess_ReturnsHistogram()
    {
        var (_, _, project) = await SeedFullStack();

        var result = await _query.GetErrorObjectsHistogram(
            project.Id, "", DateTime.UtcNow.AddDays(-7), DateTime.UtcNow,
            MakePrincipal("admin-uid"), _authz, _clickHouse, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("ReadErrorObjectsHistogramAsync", _clickHouse.LastCalledMethod);
    }

    // ── SearchSessions ─────────────────────────────────────────────

    [Fact]
    public async Task SearchSessions_WithAccess_ReturnsResult()
    {
        var (_, _, project) = await SeedFullStack();

        var result = await _query.SearchSessions(
            project.Id, "", DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, 25, 0, null, true,
            MakePrincipal("admin-uid"), _authz, _clickHouse, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("QuerySessionIdsAsync", _clickHouse.LastCalledMethod);
    }

    [Fact]
    public async Task SearchSessions_CustomSort_PassesSortField()
    {
        var (_, _, project) = await SeedFullStack();

        await _query.SearchSessions(
            project.Id, "", DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, 25, 0, "ActiveLength", false,
            MakePrincipal("admin-uid"), _authz, _clickHouse, CancellationToken.None);

        Assert.Equal("QuerySessionIdsAsync", _clickHouse.LastCalledMethod);
    }

    [Fact]
    public async Task SearchSessions_NoAccess_ThrowsGraphQLException()
    {
        var (_, _, project) = await SeedFullStack();
        var outsider = new Admin { Uid = "outsider4", Email = "outsider4@test.com" };
        _db.Admins.Add(outsider);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _query.SearchSessions(project.Id, "", DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, 25, 0, null, true,
                MakePrincipal("outsider4"), _authz, _clickHouse, CancellationToken.None));
    }

    // ── Stub IClickHouseService ────────────────────────────────────

    private class StubClickHouseService : IClickHouseService
    {
        public string? LastCalledMethod { get; private set; }
        public int LastProjectId { get; private set; }
        public string? LastAggregator { get; private set; }

        public Task<LogConnection> ReadLogsAsync(int projectId, QueryInput query, ClickHousePagination pagination, CancellationToken ct)
        {
            LastCalledMethod = nameof(ReadLogsAsync);
            LastProjectId = projectId;
            return Task.FromResult(new LogConnection());
        }

        public Task<List<HistogramBucket>> ReadLogsHistogramAsync(int projectId, QueryInput query, CancellationToken ct)
        {
            LastCalledMethod = nameof(ReadLogsHistogramAsync);
            LastProjectId = projectId;
            return Task.FromResult(new List<HistogramBucket>());
        }

        public Task<List<string>> GetLogKeysAsync(int projectId, QueryInput query, CancellationToken ct)
        {
            LastCalledMethod = nameof(GetLogKeysAsync);
            LastProjectId = projectId;
            return Task.FromResult(new List<string>());
        }

        public Task<List<string>> GetLogKeyValuesAsync(int projectId, string key, QueryInput query, CancellationToken ct)
        {
            LastCalledMethod = nameof(GetLogKeyValuesAsync);
            LastProjectId = projectId;
            return Task.FromResult(new List<string>());
        }

        public Task<TraceConnection> ReadTracesAsync(int projectId, QueryInput query, ClickHousePagination pagination, bool omitBody, CancellationToken ct)
        {
            LastCalledMethod = nameof(ReadTracesAsync);
            LastProjectId = projectId;
            return Task.FromResult(new TraceConnection());
        }

        public Task<List<HistogramBucket>> ReadTracesHistogramAsync(int projectId, QueryInput query, CancellationToken ct)
        {
            LastCalledMethod = nameof(ReadTracesHistogramAsync);
            LastProjectId = projectId;
            return Task.FromResult(new List<HistogramBucket>());
        }

        public Task<List<string>> GetTraceKeysAsync(int projectId, QueryInput query, CancellationToken ct)
        {
            LastCalledMethod = nameof(GetTraceKeysAsync);
            LastProjectId = projectId;
            return Task.FromResult(new List<string>());
        }

        public Task<List<string>> GetTraceKeyValuesAsync(int projectId, string key, QueryInput query, CancellationToken ct)
        {
            LastCalledMethod = nameof(GetTraceKeyValuesAsync);
            LastProjectId = projectId;
            return Task.FromResult(new List<string>());
        }

        public Task<(List<int> Ids, long Total)> QuerySessionIdsAsync(int projectId, QueryInput query, int count, int page, string? sortField, bool sortDesc, CancellationToken ct)
        {
            LastCalledMethod = nameof(QuerySessionIdsAsync);
            LastProjectId = projectId;
            return Task.FromResult((new List<int>(), 0L));
        }

        public Task<(List<int> Ids, long Total)> QueryErrorGroupIdsAsync(int projectId, QueryInput query, int count, int page, CancellationToken ct)
        {
            LastCalledMethod = nameof(QueryErrorGroupIdsAsync);
            LastProjectId = projectId;
            return Task.FromResult((new List<int>(), 0L));
        }

        public Task<List<HistogramBucket>> ReadErrorObjectsHistogramAsync(int projectId, QueryInput query, CancellationToken ct)
        {
            LastCalledMethod = nameof(ReadErrorObjectsHistogramAsync);
            LastProjectId = projectId;
            return Task.FromResult(new List<HistogramBucket>());
        }

        public Task<MetricsBuckets> ReadMetricsAsync(int projectId, QueryInput query, string bucketBy, List<string>? groupBy, string aggregator, string? column, CancellationToken ct)
        {
            LastCalledMethod = nameof(ReadMetricsAsync);
            LastProjectId = projectId;
            LastAggregator = aggregator;
            return Task.FromResult(new MetricsBuckets());
        }
    }
}
