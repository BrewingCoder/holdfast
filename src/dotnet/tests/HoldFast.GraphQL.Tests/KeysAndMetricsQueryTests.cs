using System.Security.Claims;
using System.Text;
using HoldFast.Data;
using HoldFast.Data.ClickHouse;
using HoldFast.Data.ClickHouse.Models;
using HoldFast.Domain.Entities;
using HoldFast.GraphQL.Private;
using HoldFast.Shared.Auth;
using HoldFast.Storage;
using HotChocolate;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HoldFast.GraphQL.Tests;

/// <summary>
/// Tests for generic keys, key values, key value suggestions, deprecated metrics queries,
/// event sessions, cross-entity correlation, source map uploads, and log lines.
/// Over-tests with edge cases, forced failures, and boundary conditions.
/// </summary>
public class KeysAndMetricsQueryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly PrivateQuery _query;
    private readonly Workspace _workspace;
    private readonly Project _project;
    private readonly Session _session;
    private readonly Admin _admin;
    private readonly ClaimsPrincipal _principal;
    private readonly FakeAuthorizationService _authz;
    private readonly FakeStorageService _storage;
    private readonly TestableClickHouseService _clickHouse;

    public KeysAndMetricsQueryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options;
        _db = new HoldFastDbContext(options);
        _db.Database.EnsureCreated();

        _admin = new Admin { Uid = "test-uid-keys", Name = "Keys Admin", Email = "keys@test.com" };
        _db.Set<Admin>().Add(_admin);
        _db.SaveChanges();

        _workspace = new Workspace { Name = "KeysWS", PlanTier = "Enterprise" };
        _db.Workspaces.Add(_workspace);
        _db.SaveChanges();

        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
        {
            AdminId = _admin.Id, WorkspaceId = _workspace.Id,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        _db.SaveChanges();

        _project = new Project { Name = "KeysProj", WorkspaceId = _workspace.Id, Secret = "keys-secret-123" };
        _db.Projects.Add(_project);
        _db.SaveChanges();

        _session = new Session
        {
            SecureId = "sess-keys-001", ProjectId = _project.Id,
            CreatedAt = DateTime.UtcNow, ActiveLength = 60000, Length = 120000,
        };
        _db.Sessions.Add(_session);
        _db.SaveChanges();

        _query = new PrivateQuery();

        var claims = new[] { new Claim(HoldFastClaimTypes.Uid, "test-uid-keys") };
        var identity = new ClaimsIdentity(claims, "Test");
        _principal = new ClaimsPrincipal(identity);

        _authz = new FakeAuthorizationService();
        _storage = new FakeStorageService();
        _clickHouse = new TestableClickHouseService();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    // ══════════════════════════════════════════════════════════════════
    // GetKeys (generic)
    // ══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("LOGS")]
    [InlineData("TRACES")]
    [InlineData("ERRORS")]
    [InlineData("SESSIONS")]
    [InlineData("EVENTS")]
    public async Task GetKeys_AllProductTypes_ReturnsResults(string productType)
    {
        var result = await _query.GetKeys(
            productType, _project.Id,
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow,
            null, null, null, _principal, _authz, _clickHouse, CancellationToken.None);

        Assert.NotNull(result);
        Assert.IsType<List<QueryKey>>(result);
    }

    [Fact]
    public async Task GetKeys_NullProductType_DefaultsToSessions()
    {
        var result = await _query.GetKeys(
            null, _project.Id,
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow,
            null, null, null, _principal, _authz, _clickHouse, CancellationToken.None);

        // Sessions keys include reserved keys like "identifier", "city", etc.
        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task GetKeys_CaseInsensitiveProductType()
    {
        var lower = await _query.GetKeys(
            "logs", _project.Id,
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow,
            null, null, null, _principal, _authz, _clickHouse, CancellationToken.None);

        var upper = await _query.GetKeys(
            "LOGS", _project.Id,
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow,
            null, null, null, _principal, _authz, _clickHouse, CancellationToken.None);

        Assert.Equal(lower.Count, upper.Count);
    }

    [Fact]
    public async Task GetKeys_LogsProductType_WrapsStringKeysInQueryKey()
    {
        _clickHouse.LogKeysResult = ["timestamp", "severity", "body"];

        var result = await _query.GetKeys(
            "LOGS", _project.Id,
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow,
            null, null, null, _principal, _authz, _clickHouse, CancellationToken.None);

        Assert.Equal(3, result.Count);
        Assert.All(result, k => Assert.Equal("String", k.Type));
        Assert.Contains(result, k => k.Name == "timestamp");
    }

    [Fact]
    public async Task GetKeys_TracesProductType_WrapsStringKeysInQueryKey()
    {
        _clickHouse.TraceKeysResult = ["span_id", "duration"];

        var result = await _query.GetKeys(
            "TRACES", _project.Id,
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow,
            null, null, null, _principal, _authz, _clickHouse, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, k => k.Name == "span_id");
    }

    // ══════════════════════════════════════════════════════════════════
    // GetKeyValues (generic)
    // ══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("LOGS")]
    [InlineData("TRACES")]
    [InlineData("ERRORS")]
    [InlineData("SESSIONS")]
    [InlineData("EVENTS")]
    public async Task GetKeyValues_AllProductTypes_ReturnsResults(string productType)
    {
        var result = await _query.GetKeyValues(
            productType, _project.Id, "test_key",
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow,
            null, null, null, _principal, _authz, _clickHouse, CancellationToken.None);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetKeyValues_NullProductType_DefaultsToSessions()
    {
        var result = await _query.GetKeyValues(
            null, _project.Id, "identifier",
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow,
            null, null, null, _principal, _authz, _clickHouse, CancellationToken.None);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetKeyValues_LogsProductType_DelegatesToClickHouse()
    {
        _clickHouse.LogKeyValuesResult = ["value1", "value2"];

        var result = await _query.GetKeyValues(
            "LOGS", _project.Id, "severity",
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow,
            null, null, null, _principal, _authz, _clickHouse, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Contains("value1", result);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetKeyValuesSuggestions
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetKeyValuesSuggestions_ReturnsOneEntryPerKey()
    {
        _clickHouse.SessionKeyValuesResult = ["val1", "val2"];

        var result = await _query.GetKeyValuesSuggestions(
            "SESSIONS", _project.Id,
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow,
            ["key1", "key2"], _principal, _authz, _clickHouse, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("key1", result[0].Key);
        Assert.Equal("key2", result[1].Key);
    }

    [Fact]
    public async Task GetKeyValuesSuggestions_EmptyKeys_ReturnsEmpty()
    {
        var result = await _query.GetKeyValuesSuggestions(
            "LOGS", _project.Id,
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow,
            [], _principal, _authz, _clickHouse, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetKeyValuesSuggestions_ValuesHaveSequentialRank()
    {
        _clickHouse.SessionKeyValuesResult = ["a", "b", "c"];

        var result = await _query.GetKeyValuesSuggestions(
            "SESSIONS", _project.Id,
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow,
            ["mykey"], _principal, _authz, _clickHouse, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(3, result[0].Values.Count);
        Assert.Equal(0, result[0].Values[0].Rank);
        Assert.Equal(1, result[0].Values[1].Rank);
        Assert.Equal(2, result[0].Values[2].Rank);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetLogsKeys / GetLogsKeyValues
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetLogsKeys_WrapsStringsAsQueryKeys()
    {
        _clickHouse.LogKeysResult = ["level", "message"];

        var result = await _query.GetLogsKeys(
            _project.Id, DateTime.UtcNow.AddDays(-7), DateTime.UtcNow,
            null, _principal, _authz, _clickHouse, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.All(result, k => Assert.Equal("String", k.Type));
    }

    [Fact]
    public async Task GetLogsKeys_EmptyResult()
    {
        _clickHouse.LogKeysResult = [];

        var result = await _query.GetLogsKeys(
            _project.Id, DateTime.UtcNow.AddDays(-7), DateTime.UtcNow,
            null, _principal, _authz, _clickHouse, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetLogsKeyValues_ReturnsValues()
    {
        _clickHouse.LogKeyValuesResult = ["error", "warn", "info"];

        var result = await _query.GetLogsKeyValues(
            _project.Id, "severity",
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow,
            null, null, _principal, _authz, _clickHouse, CancellationToken.None);

        Assert.Equal(3, result.Count);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetTracesKeys / GetTracesKeyValues
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetTracesKeys_WrapsStringsAsQueryKeys()
    {
        _clickHouse.TraceKeysResult = ["span_name", "service"];

        var result = await _query.GetTracesKeys(
            _project.Id, DateTime.UtcNow.AddDays(-7), DateTime.UtcNow,
            null, _principal, _authz, _clickHouse, CancellationToken.None);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetTracesKeyValues_ReturnsValues()
    {
        _clickHouse.TraceKeyValuesResult = ["GET /api/users", "POST /api/data"];

        var result = await _query.GetTracesKeyValues(
            _project.Id, "span_name",
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow,
            null, null, _principal, _authz, _clickHouse, CancellationToken.None);

        Assert.Equal(2, result.Count);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetErrorsKeys (new overload with dates)
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetErrorsKeys_WithDates_DelegatesToClickHouse()
    {
        _clickHouse.ErrorKeysResult = [
            new QueryKey { Name = "event", Type = "String" },
            new QueryKey { Name = "type", Type = "String" },
        ];

        var result = await _query.GetErrorsKeys(
            _project.Id, DateTime.UtcNow.AddDays(-7), DateTime.UtcNow,
            null, _principal, _authz, _clickHouse, CancellationToken.None);

        Assert.Equal(2, result.Count);
    }

    // ══════════════════════════════════════════════════════════════════
    // Deprecated Metrics Queries
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetLogsMetrics_DelegatesToReadMetrics()
    {
        var result = await _query.GetLogsMetrics(
            _project.Id,
            new QueryInput { DateRangeStart = DateTime.UtcNow.AddDays(-7), DateRangeEnd = DateTime.UtcNow },
            ["service_name"], "Timestamp", null,
            _principal, _authz, _clickHouse, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("ReadMetricsAsync", _clickHouse.LastCalledMethod);
    }

    [Fact]
    public async Task GetTracesMetrics_DelegatesToReadMetrics()
    {
        var result = await _query.GetTracesMetrics(
            _project.Id,
            new QueryInput { DateRangeStart = DateTime.UtcNow.AddDays(-7), DateRangeEnd = DateTime.UtcNow },
            ["span_name"], null, null,
            _principal, _authz, _clickHouse, CancellationToken.None);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetTracesMetrics_NullBucketBy_DefaultsToTimestamp()
    {
        await _query.GetTracesMetrics(
            _project.Id,
            new QueryInput { DateRangeStart = DateTime.UtcNow.AddDays(-7), DateRangeEnd = DateTime.UtcNow },
            [], null, null,
            _principal, _authz, _clickHouse, CancellationToken.None);

        Assert.Equal("Timestamp", _clickHouse.LastBucketBy);
    }

    [Fact]
    public async Task GetErrorsMetrics_DelegatesToReadMetrics()
    {
        var result = await _query.GetErrorsMetrics(
            _project.Id,
            new QueryInput { DateRangeStart = DateTime.UtcNow.AddDays(-7), DateRangeEnd = DateTime.UtcNow },
            [], "Timestamp", null,
            _principal, _authz, _clickHouse, CancellationToken.None);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetSessionsMetrics_DelegatesToReadMetrics()
    {
        var result = await _query.GetSessionsMetrics(
            _project.Id,
            new QueryInput { DateRangeStart = DateTime.UtcNow.AddDays(-7), DateRangeEnd = DateTime.UtcNow },
            [], "Timestamp", null,
            _principal, _authz, _clickHouse, CancellationToken.None);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetEventsMetrics_DelegatesToReadMetrics()
    {
        var result = await _query.GetEventsMetrics(
            _project.Id,
            new QueryInput { DateRangeStart = DateTime.UtcNow.AddDays(-7), DateRangeEnd = DateTime.UtcNow },
            ["event_name"], "Timestamp", null,
            _principal, _authz, _clickHouse, CancellationToken.None);

        Assert.NotNull(result);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetEventSessions
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetEventSessions_NoMatchingSessions_ReturnsEmpty()
    {
        var result = await _query.GetEventSessions(
            _project.Id, 10,
            new QueryInput { DateRangeStart = DateTime.UtcNow.AddDays(-7), DateRangeEnd = DateTime.UtcNow },
            null, true, null,
            _principal, _authz, _clickHouse, _db, CancellationToken.None);

        Assert.Empty(result.Sessions);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalLength);
        Assert.Equal(0, result.TotalActiveLength);
    }

    [Fact]
    public async Task GetEventSessions_WithMatchingIds_ReturnsSessions()
    {
        _clickHouse.SessionIdsResult = ([_session.Id], 1);

        var result = await _query.GetEventSessions(
            _project.Id, 10,
            new QueryInput { DateRangeStart = DateTime.UtcNow.AddDays(-7), DateRangeEnd = DateTime.UtcNow },
            null, true, null,
            _principal, _authz, _clickHouse, _db, CancellationToken.None);

        Assert.Single(result.Sessions);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(120000, result.TotalLength);
        Assert.Equal(60000, result.TotalActiveLength);
    }

    [Fact]
    public async Task GetEventSessions_PreservesClickHouseOrdering()
    {
        var s2 = new Session { SecureId = "sess-keys-002", ProjectId = _project.Id, CreatedAt = DateTime.UtcNow, ActiveLength = 10000, Length = 20000 };
        var s3 = new Session { SecureId = "sess-keys-003", ProjectId = _project.Id, CreatedAt = DateTime.UtcNow, ActiveLength = 5000, Length = 10000 };
        _db.Sessions.AddRange(s2, s3);
        _db.SaveChanges();

        // ClickHouse returns IDs in specific order: s3, s2, s1
        _clickHouse.SessionIdsResult = ([s3.Id, s2.Id, _session.Id], 3);

        var result = await _query.GetEventSessions(
            _project.Id, 10,
            new QueryInput { DateRangeStart = DateTime.UtcNow.AddDays(-7), DateRangeEnd = DateTime.UtcNow },
            null, true, null,
            _principal, _authz, _clickHouse, _db, CancellationToken.None);

        Assert.Equal(3, result.Sessions.Count);
        Assert.Equal(s3.Id, result.Sessions[0].Id);
        Assert.Equal(s2.Id, result.Sessions[1].Id);
        Assert.Equal(_session.Id, result.Sessions[2].Id);
    }

    [Fact]
    public async Task GetEventSessions_NullLengths_TreatedAsZero()
    {
        var s = new Session { SecureId = "sess-null-len-ev", ProjectId = _project.Id, CreatedAt = DateTime.UtcNow, ActiveLength = null, Length = null };
        _db.Sessions.Add(s);
        _db.SaveChanges();

        _clickHouse.SessionIdsResult = ([s.Id], 1);

        var result = await _query.GetEventSessions(
            _project.Id, 10,
            new QueryInput { DateRangeStart = DateTime.UtcNow.AddDays(-7), DateRangeEnd = DateTime.UtcNow },
            null, true, null,
            _principal, _authz, _clickHouse, _db, CancellationToken.None);

        Assert.Equal(0, result.TotalLength);
        Assert.Equal(0, result.TotalActiveLength);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetExistingLogsTraces
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetExistingLogsTraces_EmptyTraceIds_ReturnsEmpty()
    {
        var result = await _query.GetExistingLogsTraces(
            _project.Id, [],
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow,
            _principal, _authz, _clickHouse, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetExistingLogsTraces_NoMatchingLogs_ReturnsEmpty()
    {
        // Default fake returns empty LogConnection
        var result = await _query.GetExistingLogsTraces(
            _project.Id, ["trace-abc", "trace-def"],
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow,
            _principal, _authz, _clickHouse, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetExistingLogsTraces_MatchingLogs_ReturnsTraceIds()
    {
        _clickHouse.LogsForTraceIds["trace-found"] = new LogConnection
        {
            Edges = [new LogEdge { Node = new LogRow { Body = "test" }, Cursor = "c1" }],
        };

        var result = await _query.GetExistingLogsTraces(
            _project.Id, ["trace-found", "trace-missing"],
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow,
            _principal, _authz, _clickHouse, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("trace-found", result[0]);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetLogsErrorObjects
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetLogsErrorObjects_EmptyCursors_ReturnsEmpty()
    {
        var result = await _query.GetLogsErrorObjects([], _db, CancellationToken.None);

        Assert.Empty(result);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetSourceMapUploadUrls
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetSourceMapUploadUrls_ValidApiKey_ReturnsUrls()
    {
        var result = await _query.GetSourceMapUploadUrls(
            "keys-secret-123", ["app.js.map", "vendor.js.map"],
            _db, _storage, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.All(result, url => Assert.Contains("sourcemaps", url));
    }

    [Fact]
    public async Task GetSourceMapUploadUrls_InvalidApiKey_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _query.GetSourceMapUploadUrls("bad-key", ["file.map"], _db, _storage, CancellationToken.None));
    }

    [Fact]
    public async Task GetSourceMapUploadUrls_EmptyPaths_ReturnsEmpty()
    {
        var result = await _query.GetSourceMapUploadUrls(
            "keys-secret-123", [], _db, _storage, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSourceMapUploadUrls_UrlContainsProjectIdAndPath()
    {
        var result = await _query.GetSourceMapUploadUrls(
            "keys-secret-123", ["src/app.js.map"],
            _db, _storage, CancellationToken.None);

        Assert.Single(result);
        Assert.Contains($"{_project.Id}", result[0]);
        Assert.Contains("src/app.js.map", result[0]);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetLogLines
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetLogLines_EmptyLogs_ReturnsEmpty()
    {
        var result = await _query.GetLogLines(
            "LOGS", _project.Id,
            new QueryInput { DateRangeStart = DateTime.UtcNow.AddDays(-7), DateRangeEnd = DateTime.UtcNow },
            _principal, _authz, _clickHouse, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetLogLines_WithLogs_ReturnsStructuredLines()
    {
        _clickHouse.LogConnectionResult = new LogConnection
        {
            Edges =
            [
                new LogEdge
                {
                    Node = new LogRow
                    {
                        Timestamp = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc),
                        Body = "User logged in",
                        SeverityText = "INFO",
                        LogAttributes = new Dictionary<string, string> { ["user"] = "alice" },
                    },
                    Cursor = "c1",
                },
                new LogEdge
                {
                    Node = new LogRow
                    {
                        Timestamp = new DateTime(2026, 3, 1, 12, 1, 0, DateTimeKind.Utc),
                        Body = "Error occurred",
                        SeverityText = "ERROR",
                        LogAttributes = new Dictionary<string, string> { ["code"] = "500" },
                    },
                    Cursor = "c2",
                },
            ],
        };

        var result = await _query.GetLogLines(
            "LOGS", _project.Id,
            new QueryInput { DateRangeStart = DateTime.UtcNow.AddDays(-7), DateRangeEnd = DateTime.UtcNow },
            _principal, _authz, _clickHouse, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("User logged in", result[0].Body);
        Assert.Equal("INFO", result[0].Severity);
        Assert.Contains("alice", result[0].Labels);
        Assert.Equal("Error occurred", result[1].Body);
    }

    [Fact]
    public async Task GetLogLines_NullBody_DefaultsToEmpty()
    {
        _clickHouse.LogConnectionResult = new LogConnection
        {
            Edges =
            [
                new LogEdge
                {
                    Node = new LogRow { Timestamp = DateTime.UtcNow, Body = null!, SeverityText = "WARN" },
                    Cursor = "c1",
                },
            ],
        };

        var result = await _query.GetLogLines(
            "LOGS", _project.Id,
            new QueryInput { DateRangeStart = DateTime.UtcNow.AddDays(-7), DateRangeEnd = DateTime.UtcNow },
            _principal, _authz, _clickHouse, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("", result[0].Body);
    }

    // ══════════════════════════════════════════════════════════════════
    // SearchIssues (stub)
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SearchIssues_ReturnsEmptyList()
    {
        var result = await _query.SearchIssues(
            Domain.Enums.IntegrationType.Linear, _project.Id, "bug", _principal);

        Assert.Empty(result);
    }

    [Fact]
    public async Task SearchIssues_AllIntegrationTypes_ReturnEmpty()
    {
        foreach (var intType in Enum.GetValues<Domain.Enums.IntegrationType>())
        {
            var result = await _query.SearchIssues(intType, _project.Id, "test query", _principal);
            Assert.Empty(result);
        }
    }

    [Fact]
    public async Task SearchIssues_EmptyQuery_ReturnsEmpty()
    {
        var result = await _query.SearchIssues(
            Domain.Enums.IntegrationType.GitHub, _project.Id, "", _principal);

        Assert.Empty(result);
    }

    // ══════════════════════════════════════════════════════════════════
    // GenerateZapierAccessToken
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GenerateZapierAccessToken_ReturnsNonEmptyToken()
    {
        var result = await _query.GenerateZapierAccessToken(_project.Id, _principal);

        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task GenerateZapierAccessToken_IsValidBase64()
    {
        var result = await _query.GenerateZapierAccessToken(_project.Id, _principal);

        var bytes = Convert.FromBase64String(result);
        var decoded = Encoding.UTF8.GetString(bytes);
        Assert.StartsWith("zapier:", decoded);
    }

    [Fact]
    public async Task GenerateZapierAccessToken_ContainsProjectId()
    {
        var result = await _query.GenerateZapierAccessToken(_project.Id, _principal);

        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(result));
        Assert.Contains($"{_project.Id}", decoded);
    }

    [Fact]
    public async Task GenerateZapierAccessToken_DifferentProjectIds_DifferentTokens()
    {
        var token1 = await _query.GenerateZapierAccessToken(1, _principal);
        var token2 = await _query.GenerateZapierAccessToken(2, _principal);

        Assert.NotEqual(token1, token2);
    }

    // ══════════════════════════════════════════════════════════════════
    // Fakes
    // ══════════════════════════════════════════════════════════════════

    private class FakeAuthorizationService : IAuthorizationService
    {
        private readonly Admin _admin = new() { Uid = "test-uid-keys", Name = "Keys Admin", Email = "keys@test.com" };

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

    private class FakeStorageService : IStorageService
    {
        public Task UploadAsync(string bucket, string key, Stream data, string? contentType = null, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<Stream?> DownloadAsync(string bucket, string key, CancellationToken ct = default)
            => Task.FromResult<Stream?>(null);
        public Task<bool> ExistsAsync(string bucket, string key, CancellationToken ct = default)
            => Task.FromResult(false);
        public Task DeleteAsync(string bucket, string key, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<string> GetDownloadUrlAsync(string bucket, string key, TimeSpan expiry, CancellationToken ct = default)
            => Task.FromResult($"file://{bucket}/{key}");
    }

    /// <summary>
    /// Testable ClickHouse service that tracks calls and allows configuring return values.
    /// </summary>
    private class TestableClickHouseService : IClickHouseService
    {
        public string? LastCalledMethod { get; private set; }
        public string? LastBucketBy { get; private set; }

        // Configurable return values
        public List<string> LogKeysResult { get; set; } = [];
        public List<string> LogKeyValuesResult { get; set; } = [];
        public List<string> TraceKeysResult { get; set; } = [];
        public List<string> TraceKeyValuesResult { get; set; } = [];
        public List<QueryKey> ErrorKeysResult { get; set; } = [];
        public List<string> SessionKeyValuesResult { get; set; } = [];
        public (List<int> Ids, long Total) SessionIdsResult { get; set; } = ([], 0);
        public Dictionary<string, LogConnection> LogsForTraceIds { get; } = new();
        public LogConnection LogConnectionResult { get; set; } = new();

        public Task<MetricsBuckets> ReadMetricsAsync(int projectId, QueryInput query, string bucketBy, List<string>? groupBy, string aggregator, string? column, CancellationToken ct)
        {
            LastCalledMethod = nameof(ReadMetricsAsync);
            LastBucketBy = bucketBy;
            return Task.FromResult(new MetricsBuckets());
        }

        public Task<List<string>> GetLogKeysAsync(int projectId, QueryInput query, CancellationToken ct)
            => Task.FromResult(LogKeysResult);
        public Task<List<string>> GetLogKeyValuesAsync(int projectId, string key, QueryInput query, CancellationToken ct)
            => Task.FromResult(LogKeyValuesResult);
        public Task<List<string>> GetTraceKeysAsync(int projectId, QueryInput query, CancellationToken ct)
            => Task.FromResult(TraceKeysResult);
        public Task<List<string>> GetTraceKeyValuesAsync(int projectId, string key, QueryInput query, CancellationToken ct)
            => Task.FromResult(TraceKeyValuesResult);
        public Task<List<QueryKey>> GetErrorsKeysAsync(int projectId, DateTime startDate, DateTime endDate, string? query, CancellationToken ct)
            => Task.FromResult(ErrorKeysResult);
        public Task<List<string>> GetErrorsKeyValuesAsync(int projectId, string keyName, DateTime startDate, DateTime endDate, string? query, int? count, CancellationToken ct)
            => Task.FromResult(new List<string>());
        public Task<List<QueryKey>> GetSessionsKeysAsync(int projectId, DateTime startDate, DateTime endDate, string? query, CancellationToken ct)
        {
            // Return reserved session keys like the real implementation
            var keys = new[] { "identifier", "city", "state", "country", "os_name" }
                .Where(k => string.IsNullOrEmpty(query) || k.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Select(k => new QueryKey { Name = k, Type = "String" }).ToList();
            return Task.FromResult(keys);
        }
        public Task<List<string>> GetSessionsKeyValuesAsync(int projectId, string keyName, DateTime startDate, DateTime endDate, string? query, int? count, CancellationToken ct)
            => Task.FromResult(SessionKeyValuesResult);
        public Task<List<QueryKey>> GetEventsKeysAsync(int projectId, DateTime startDate, DateTime endDate, string? query, string? eventName, CancellationToken ct)
        {
            var keys = new[] { "event", "timestamp", "session_id" }
                .Select(k => new QueryKey { Name = k, Type = "String" }).ToList();
            return Task.FromResult(keys);
        }
        public Task<List<string>> GetEventsKeyValuesAsync(int projectId, string keyName, DateTime startDate, DateTime endDate, string? query, int? count, string? eventName, CancellationToken ct)
            => Task.FromResult(new List<string>());

        public Task<LogConnection> ReadLogsAsync(int projectId, QueryInput query, ClickHousePagination pagination, CancellationToken ct)
        {
            // Check if this is a trace ID lookup
            if (query.Query?.StartsWith("trace_id=") == true)
            {
                var traceId = query.Query.Split('=')[1].Split(' ')[0];
                if (LogsForTraceIds.TryGetValue(traceId, out var conn))
                    return Task.FromResult(conn);
            }
            return Task.FromResult(LogConnectionResult);
        }

        public Task<List<HistogramBucket>> ReadLogsHistogramAsync(int projectId, QueryInput query, CancellationToken ct) => Task.FromResult(new List<HistogramBucket>());
        public Task<TraceConnection> ReadTracesAsync(int projectId, QueryInput query, ClickHousePagination pagination, bool omitBody, CancellationToken ct) => Task.FromResult(new TraceConnection());
        public Task<List<HistogramBucket>> ReadTracesHistogramAsync(int projectId, QueryInput query, CancellationToken ct) => Task.FromResult(new List<HistogramBucket>());
        public Task<List<HistogramBucket>> ReadSessionsHistogramAsync(int projectId, QueryInput query, CancellationToken ct) => Task.FromResult(new List<HistogramBucket>());
        public Task<(List<int> Ids, long Total)> QuerySessionIdsAsync(int projectId, QueryInput query, int count, int page, string? sortField, bool sortDesc, CancellationToken ct)
            => Task.FromResult(SessionIdsResult);
        public Task<(List<int> Ids, long Total)> QueryErrorGroupIdsAsync(int projectId, QueryInput query, int count, int page, CancellationToken ct) => Task.FromResult((new List<int>(), 0L));
        public Task<List<HistogramBucket>> ReadErrorObjectsHistogramAsync(int projectId, QueryInput query, CancellationToken ct) => Task.FromResult(new List<HistogramBucket>());
        public Task WriteMetricAsync(int projectId, string metricName, double metricValue, string? category, DateTime timestamp, Dictionary<string, string>? tags, string? sessionSecureId, CancellationToken ct) => Task.CompletedTask;
        public Task WriteLogsAsync(IEnumerable<LogRowInput> logs, CancellationToken ct) => Task.CompletedTask;
        public Task WriteTracesAsync(IEnumerable<TraceRowInput> traces, CancellationToken ct) => Task.CompletedTask;
        public Task WriteSessionsAsync(IEnumerable<SessionRowInput> sessions, CancellationToken ct) => Task.CompletedTask;
        public Task WriteErrorGroupsAsync(IEnumerable<ErrorGroupRowInput> errorGroups, CancellationToken ct) => Task.CompletedTask;
        public Task WriteErrorObjectsAsync(IEnumerable<ErrorObjectRowInput> errorObjects, CancellationToken ct) => Task.CompletedTask;
    }
}
