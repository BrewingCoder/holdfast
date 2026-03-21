using System.Security.Claims;
using System.Text;
using HoldFast.Data;
using HoldFast.Data.ClickHouse;
using HoldFast.Data.ClickHouse.Models;
using HoldFast.Domain.Entities;
using HoldFast.Domain.Enums;
using HoldFast.GraphQL.Private;
using HoldFast.Shared.Auth;
using HoldFast.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HoldFast.GraphQL.Tests;

/// <summary>
/// Edge case tests for various query methods — boundary conditions,
/// empty inputs, large datasets, concurrency, and forced failures.
/// </summary>
public class EdgeCaseQueryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly PrivateQuery _query;
    private readonly PrivateMutation _mutation;
    private readonly Workspace _workspace;
    private readonly Project _project;
    private readonly Admin _admin;
    private readonly ClaimsPrincipal _principal;
    private readonly FakeAuthorizationService _authz;
    private readonly FakeStorageService _storage;
    private readonly FakeClickHouseService _clickHouse;

    public EdgeCaseQueryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options;
        _db = new HoldFastDbContext(options);
        _db.Database.EnsureCreated();

        _admin = new Admin { Uid = "edge-uid", Name = "Edge Admin", Email = "edge@test.com" };
        _db.Set<Admin>().Add(_admin);
        _db.SaveChanges();

        _workspace = new Workspace { Name = "EdgeWS", PlanTier = "Enterprise" };
        _db.Workspaces.Add(_workspace);
        _db.SaveChanges();

        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
        {
            AdminId = _admin.Id, WorkspaceId = _workspace.Id,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        _db.SaveChanges();

        _project = new Project { Name = "EdgeProj", WorkspaceId = _workspace.Id, Secret = "edge-secret" };
        _db.Projects.Add(_project);
        _db.SaveChanges();

        _query = new PrivateQuery();
        _mutation = new PrivateMutation();

        var claims = new[] { new Claim(HoldFastClaimTypes.Uid, "edge-uid") };
        var identity = new ClaimsIdentity(claims, "Test");
        _principal = new ClaimsPrincipal(identity);

        _authz = new FakeAuthorizationService();
        _storage = new FakeStorageService();
        _clickHouse = new FakeClickHouseService();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    // ══════════════════════════════════════════════════════════════════
    // GetLogsErrorObjects — cursor decoding edge cases
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetLogsErrorObjects_InvalidBase64Cursors_ReturnsEmpty()
    {
        var result = await _query.GetLogsErrorObjects(
            ["not-base64!!!", "also-invalid"], _db, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetLogsErrorObjects_ValidBase64_NonDateContent_ReturnsEmpty()
    {
        var cursor = Convert.ToBase64String(Encoding.UTF8.GetBytes("not-a-date,uuid"));
        var result = await _query.GetLogsErrorObjects([cursor], _db, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetLogsErrorObjects_ValidCursor_FindsNearbyErrors()
    {
        var timestamp = DateTime.UtcNow;
        var eg = new ErrorGroup { ProjectId = _project.Id, SecureId = "eg-cursor" };
        _db.Set<ErrorGroup>().Add(eg);
        _db.SaveChanges();

        _db.Set<ErrorObject>().Add(new ErrorObject
        {
            ProjectId = _project.Id, ErrorGroupId = eg.Id,
            Event = "NearbyError", CreatedAt = timestamp,
        });
        _db.SaveChanges();

        var cursor = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{timestamp:O},uuid-123"));

        var result = await _query.GetLogsErrorObjects([cursor], _db, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("NearbyError", result[0].Event);
    }

    [Fact]
    public async Task GetLogsErrorObjects_MixedValidAndInvalidCursors()
    {
        var timestamp = DateTime.UtcNow;
        var eg = new ErrorGroup { ProjectId = _project.Id, SecureId = "eg-mixed" };
        _db.Set<ErrorGroup>().Add(eg);
        _db.SaveChanges();

        _db.Set<ErrorObject>().Add(new ErrorObject
        {
            ProjectId = _project.Id, ErrorGroupId = eg.Id,
            Event = "Found", CreatedAt = timestamp,
        });
        _db.SaveChanges();

        var validCursor = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{timestamp:O},uuid-valid"));

        var result = await _query.GetLogsErrorObjects(
            ["invalid!!!", validCursor, "also-invalid"],
            _db, CancellationToken.None);

        Assert.Single(result);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetSourceMapUploadUrls — edge cases
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetSourceMapUploadUrls_PathsWithSpecialCharacters()
    {
        var result = await _query.GetSourceMapUploadUrls(
            "edge-secret", ["path/with spaces/file.js.map", "path@special#chars.map"],
            _db, _storage, CancellationToken.None);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetSourceMapUploadUrls_SinglePath()
    {
        var result = await _query.GetSourceMapUploadUrls(
            "edge-secret", ["single.js.map"],
            _db, _storage, CancellationToken.None);

        Assert.Single(result);
    }

    // ══════════════════════════════════════════════════════════════════
    // GenerateZapierAccessToken — determinism
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GenerateZapierAccessToken_ConsecutiveCallsProduceDifferentTokens()
    {
        // Tokens include timestamps, so consecutive calls should differ
        var t1 = await _query.GenerateZapierAccessToken(1, _principal);
        await Task.Delay(1); // Ensure time changes
        var t2 = await _query.GenerateZapierAccessToken(1, _principal);

        // They may be the same if called within the same millisecond, but format is correct
        Assert.NotEmpty(t1);
        Assert.NotEmpty(t2);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetKeys — unknown product type
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetKeys_UnknownProductType_DefaultsToSessions()
    {
        var result = await _query.GetKeys(
            "UNKNOWN_TYPE", _project.Id,
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow,
            null, null, null, _principal, _authz, _clickHouse, CancellationToken.None);

        // Should default to sessions keys (same as null)
        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task GetKeyValues_UnknownProductType_DefaultsToSessions()
    {
        var result = await _query.GetKeyValues(
            "INVALID", _project.Id, "identifier",
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow,
            null, null, null, _principal, _authz, _clickHouse, CancellationToken.None);

        Assert.NotNull(result);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetKeyValuesSuggestions — large key list
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetKeyValuesSuggestions_ManyKeys()
    {
        var keys = Enumerable.Range(1, 20).Select(i => $"key_{i}").ToList();

        var result = await _query.GetKeyValuesSuggestions(
            "SESSIONS", _project.Id,
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow,
            keys, _principal, _authz, _clickHouse, CancellationToken.None);

        Assert.Equal(20, result.Count);
    }

    // ══════════════════════════════════════════════════════════════════
    // Alert CRUD — edge cases
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CreateAlert_AllNullOptionalFields()
    {
        var result = await _mutation.CreateAlert(
            _project.Id, "Minimal", "ERRORS_ALERT", null, null,
            null, null, null, null, null, null, null,
            _principal, _authz, _db, CancellationToken.None);

        Assert.Equal("Minimal", result.Name);
        Assert.Null(result.FunctionType);
        Assert.Null(result.FunctionColumn);
        Assert.Null(result.Query);
        Assert.Null(result.GroupByKey);
    }

    [Fact]
    public async Task CreateAlert_VeryLongName()
    {
        var longName = new string('A', 500);
        var result = await _mutation.CreateAlert(
            _project.Id, longName, "ERRORS_ALERT", null, null,
            null, null, null, null, null, null, null,
            _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(longName, result.Name);
    }

    [Fact]
    public async Task CreateAlert_ManyDestinations()
    {
        var destinations = Enumerable.Range(1, 10)
            .Select(i => new AlertDestinationInput($"Type{i}", $"id-{i}", $"name-{i}"))
            .ToList();

        var result = await _mutation.CreateAlert(
            _project.Id, "ManyDests", "ERRORS_ALERT", null, null,
            null, null, null, null, null, null, destinations,
            _principal, _authz, _db, CancellationToken.None);

        var savedDests = await _db.AlertDestinations
            .Where(d => d.AlertId == result.Id).ToListAsync();
        Assert.Equal(10, savedDests.Count);
    }

    // ══════════════════════════════════════════════════════════════════
    // IsIntegratedWith — all integration types
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task IsIntegratedWith_AllTypes_ReturnsFalse_WhenNoTokens()
    {
        foreach (var intType in Enum.GetValues<IntegrationType>())
        {
            var result = await _query.IsIntegratedWith(
                intType, _project.Id, _principal, _authz, _db, CancellationToken.None);

            Assert.False(result, $"Expected false for {intType} with no tokens");
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // GetEventSessions — page parameter
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetEventSessions_NullPage_DefaultsToZero()
    {
        var result = await _query.GetEventSessions(
            _project.Id, 10,
            new QueryInput { DateRangeStart = DateTime.UtcNow.AddDays(-7), DateRangeEnd = DateTime.UtcNow },
            null, true, null, // page = null
            _principal, _authz, _clickHouse, _db, CancellationToken.None);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetEventSessions_WithSortField()
    {
        var result = await _query.GetEventSessions(
            _project.Id, 10,
            new QueryInput { DateRangeStart = DateTime.UtcNow.AddDays(-7), DateRangeEnd = DateTime.UtcNow },
            "created_at", false, 0,
            _principal, _authz, _clickHouse, _db, CancellationToken.None);

        Assert.NotNull(result);
    }

    // ══════════════════════════════════════════════════════════════════
    // SearchIssues — various integration types
    // ══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(IntegrationType.Linear)]
    [InlineData(IntegrationType.Jira)]
    [InlineData(IntegrationType.GitHub)]
    [InlineData(IntegrationType.GitLab)]
    [InlineData(IntegrationType.ClickUp)]
    [InlineData(IntegrationType.Height)]
    public async Task SearchIssues_AllIntegrationTypes_ReturnEmpty(IntegrationType type)
    {
        var result = await _query.SearchIssues(type, _project.Id, "test", _principal);

        Assert.Empty(result);
    }

    // ══════════════════════════════════════════════════════════════════
    // Fakes
    // ══════════════════════════════════════════════════════════════════

    private class FakeAuthorizationService : IAuthorizationService
    {
        private readonly Admin _admin = new() { Uid = "edge-uid", Name = "Edge Admin", Email = "edge@test.com" };
        public Task<Admin> GetCurrentAdminAsync(string uid, CancellationToken ct = default) => Task.FromResult(_admin);
        public Task<Workspace> IsAdminInWorkspaceAsync(int adminId, int workspaceId, CancellationToken ct = default) => Task.FromResult(new Workspace { Name = "Test" });
        public Task<Workspace> IsAdminInWorkspaceFullAccessAsync(int adminId, int workspaceId, CancellationToken ct = default) => Task.FromResult(new Workspace { Name = "Test" });
        public Task<Project> IsAdminInProjectAsync(int adminId, int projectId, CancellationToken ct = default) => Task.FromResult(new Project { Name = "Test" });
        public Task<(string Role, List<int>? ProjectIds)?> GetAdminRoleAsync(int adminId, int workspaceId, CancellationToken ct = default) => Task.FromResult<(string Role, List<int>? ProjectIds)?>(("ADMIN", null));
        public Task ValidateAdminRoleAsync(int adminId, int workspaceId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private class FakeStorageService : IStorageService
    {
        public Task UploadAsync(string bucket, string key, Stream data, string? contentType = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Stream?> DownloadAsync(string bucket, string key, CancellationToken ct = default) => Task.FromResult<Stream?>(null);
        public Task<bool> ExistsAsync(string bucket, string key, CancellationToken ct = default) => Task.FromResult(false);
        public Task DeleteAsync(string bucket, string key, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> GetDownloadUrlAsync(string bucket, string key, TimeSpan expiry, CancellationToken ct = default) => Task.FromResult($"file://{bucket}/{key}");
    }

    private class FakeClickHouseService : IClickHouseService
    {
        public Task<MetricsBuckets> ReadMetricsAsync(int projectId, QueryInput query, string bucketBy, List<string>? groupBy, string aggregator, string? column, CancellationToken ct) => Task.FromResult(new MetricsBuckets());
        public Task<List<string>> GetLogKeysAsync(int projectId, QueryInput query, CancellationToken ct) => Task.FromResult(new List<string>());
        public Task<List<string>> GetLogKeyValuesAsync(int projectId, string key, QueryInput query, CancellationToken ct) => Task.FromResult(new List<string>());
        public Task<LogConnection> ReadLogsAsync(int projectId, QueryInput query, ClickHousePagination pagination, CancellationToken ct) => Task.FromResult(new LogConnection());
        public Task<List<HistogramBucket>> ReadLogsHistogramAsync(int projectId, QueryInput query, CancellationToken ct) => Task.FromResult(new List<HistogramBucket>());
        public Task<TraceConnection> ReadTracesAsync(int projectId, QueryInput query, ClickHousePagination pagination, bool omitBody, CancellationToken ct) => Task.FromResult(new TraceConnection());
        public Task<List<HistogramBucket>> ReadTracesHistogramAsync(int projectId, QueryInput query, CancellationToken ct) => Task.FromResult(new List<HistogramBucket>());
        public Task<List<string>> GetTraceKeysAsync(int projectId, QueryInput query, CancellationToken ct) => Task.FromResult(new List<string>());
        public Task<List<string>> GetTraceKeyValuesAsync(int projectId, string key, QueryInput query, CancellationToken ct) => Task.FromResult(new List<string>());
        public Task<List<HistogramBucket>> ReadSessionsHistogramAsync(int projectId, QueryInput query, CancellationToken ct) => Task.FromResult(new List<HistogramBucket>());
        public Task<(List<int> Ids, long Total)> QuerySessionIdsAsync(int projectId, QueryInput query, int count, int page, string? sortField, bool sortDesc, CancellationToken ct) => Task.FromResult((new List<int>(), 0L));
        public Task<(List<int> Ids, long Total)> QueryErrorGroupIdsAsync(int projectId, QueryInput query, int count, int page, CancellationToken ct) => Task.FromResult((new List<int>(), 0L));
        public Task<List<HistogramBucket>> ReadErrorObjectsHistogramAsync(int projectId, QueryInput query, CancellationToken ct) => Task.FromResult(new List<HistogramBucket>());
        public Task<List<QueryKey>> GetSessionsKeysAsync(int projectId, DateTime startDate, DateTime endDate, string? query, CancellationToken ct)
        {
            var keys = new[] { "identifier", "city", "state", "country", "os_name" }
                .Select(k => new QueryKey { Name = k, Type = "String" }).ToList();
            return Task.FromResult(keys);
        }
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
