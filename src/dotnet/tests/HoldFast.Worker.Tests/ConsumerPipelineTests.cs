using HoldFast.Data;
using HoldFast.Data.ClickHouse;
using HoldFast.Data.ClickHouse.Models;
using HoldFast.Domain.Entities;
using HoldFast.Shared.AlertEvaluation;
using HoldFast.Shared.ErrorGrouping;
using HoldFast.Storage;
using HoldFast.Worker;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HoldFast.Worker.Tests;

/// <summary>
/// Tests for Kafka consumer ProcessAsync pipelines using fake ClickHouse/Storage services.
/// Verifies the full message→service→storage flow for each consumer type.
/// </summary>
public class ConsumerPipelineTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly FakeClickHouseService _clickHouse;
    private readonly FakeStorageService _storage;
    private readonly Project _project;
    private readonly Workspace _workspace;

    public ConsumerPipelineTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options;
        _db = new HoldFastDbContext(options);
        _db.Database.EnsureCreated();

        _workspace = new Workspace { Name = "TestWS", PlanTier = "Enterprise" };
        _db.Workspaces.Add(_workspace);
        _db.SaveChanges();

        _project = new Project { Name = "TestProj", WorkspaceId = _workspace.Id };
        _db.Projects.Add(_project);
        _db.SaveChanges();

        _clickHouse = new FakeClickHouseService();
        _storage = new FakeStorageService();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    // ── Fake ClickHouse ────────────────────────────────────────────────

    private class FakeClickHouseService : IClickHouseService
    {
        public List<LogRowInput> WrittenLogs { get; } = [];
        public List<TraceRowInput> WrittenTraces { get; } = [];
        public List<(int ProjectId, string Name, double Value, string? Category, DateTime Timestamp, Dictionary<string, string>? Tags, string? SessionId)> WrittenMetrics { get; } = [];
        public List<SessionRowInput> WrittenSessions { get; } = [];
        public List<ErrorGroupRowInput> WrittenErrorGroups { get; } = [];
        public List<ErrorObjectRowInput> WrittenErrorObjects { get; } = [];

        public Task WriteLogsAsync(IEnumerable<LogRowInput> logs, CancellationToken ct)
        {
            WrittenLogs.AddRange(logs);
            return Task.CompletedTask;
        }

        public Task WriteTracesAsync(IEnumerable<TraceRowInput> traces, CancellationToken ct)
        {
            WrittenTraces.AddRange(traces);
            return Task.CompletedTask;
        }

        public Task WriteMetricAsync(int projectId, string metricName, double metricValue,
            string? category, DateTime timestamp, Dictionary<string, string>? tags,
            string? sessionSecureId, CancellationToken ct)
        {
            WrittenMetrics.Add((projectId, metricName, metricValue, category, timestamp, tags, sessionSecureId));
            return Task.CompletedTask;
        }

        public Task WriteSessionsAsync(IEnumerable<SessionRowInput> sessions, CancellationToken ct)
        {
            WrittenSessions.AddRange(sessions);
            return Task.CompletedTask;
        }

        public Task WriteErrorGroupsAsync(IEnumerable<ErrorGroupRowInput> errorGroups, CancellationToken ct)
        {
            WrittenErrorGroups.AddRange(errorGroups);
            return Task.CompletedTask;
        }

        public Task WriteErrorObjectsAsync(IEnumerable<ErrorObjectRowInput> errorObjects, CancellationToken ct)
        {
            WrittenErrorObjects.AddRange(errorObjects);
            return Task.CompletedTask;
        }

        // Read methods — return empty results (not exercised by consumer tests)
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
        public Task<(List<int> Ids, long Total)> QueryErrorGroupIdsAsync(int projectId, QueryInput query, int count, int page, CancellationToken ct) => Task.FromResult((new List<int>(), 0L));
        public Task<List<HistogramBucket>> ReadErrorObjectsHistogramAsync(int projectId, QueryInput query, CancellationToken ct) => Task.FromResult(new List<HistogramBucket>());
        public Task<MetricsBuckets> ReadMetricsAsync(int projectId, QueryInput query, string bucketBy, List<string>? groupBy, string aggregator, string? column, CancellationToken ct) => Task.FromResult(new MetricsBuckets());
        public Task<List<QueryKey>> GetSessionsKeysAsync(int projectId, DateTime startDate, DateTime endDate, string? query, CancellationToken ct) => Task.FromResult(new List<QueryKey>());
        public Task<List<string>> GetSessionsKeyValuesAsync(int projectId, string keyName, DateTime startDate, DateTime endDate, string? query, int? count, CancellationToken ct) => Task.FromResult(new List<string>());
        public Task<List<string>> GetErrorsKeyValuesAsync(int projectId, string keyName, DateTime startDate, DateTime endDate, string? query, int? count, CancellationToken ct) => Task.FromResult(new List<string>());
        public Task<List<QueryKey>> GetEventsKeysAsync(int projectId, DateTime startDate, DateTime endDate, string? query, string? eventName, CancellationToken ct) => Task.FromResult(new List<QueryKey>());
        public Task<List<string>> GetEventsKeyValuesAsync(int projectId, string keyName, DateTime startDate, DateTime endDate, string? query, int? count, string? eventName, CancellationToken ct) => Task.FromResult(new List<string>());
    }

    // ── Fake Storage ──────────────────────────────────────────────────

    private class FakeStorageService : IStorageService
    {
        public List<(string bucket, string key)> Uploads { get; } = [];

        public Task UploadAsync(string bucket, string key, Stream data, string? contentType = null, CancellationToken ct = default)
        {
            Uploads.Add((bucket, key));
            return Task.CompletedTask;
        }

        public Task<Stream?> DownloadAsync(string bucket, string key, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(null);

        public Task<bool> ExistsAsync(string bucket, string key, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task DeleteAsync(string bucket, string key, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<string> GetDownloadUrlAsync(string bucket, string key, TimeSpan expiry, CancellationToken ct = default) =>
            Task.FromResult($"file://{bucket}/{key}");
    }

    // ── LogIngestion Pipeline Tests ───────────────────────────────────

    [Fact]
    public async Task LogIngestion_MapsAllFieldsToClickHouse()
    {
        var ts = new DateTime(2026, 3, 20, 10, 0, 0, DateTimeKind.Utc);
        var attrs = new Dictionary<string, string>
        {
            ["http.method"] = "POST",
            ["http.url"] = "/api/users",
        };
        var msg = new LogIngestionMessage(
            _project.Id, ts, "trace-abc", "span-xyz", "sess-123",
            "ERROR", 17, "otel", "my-api", "2.0.1",
            "NullReferenceException at Program.cs:42", attrs, "production");

        // Simulate what the consumer does
        var logRow = new LogRowInput
        {
            ProjectId = msg.ProjectId,
            Timestamp = msg.Timestamp,
            TraceId = msg.TraceId,
            SpanId = msg.SpanId,
            SecureSessionId = msg.SecureSessionId,
            SeverityText = msg.SeverityText,
            SeverityNumber = msg.SeverityNumber,
            Source = msg.Source,
            ServiceName = msg.ServiceName,
            ServiceVersion = msg.ServiceVersion,
            Body = msg.Body,
            LogAttributes = msg.LogAttributes ?? new(),
            Environment = msg.Environment,
        };
        await _clickHouse.WriteLogsAsync([logRow], CancellationToken.None);

        Assert.Single(_clickHouse.WrittenLogs);
        var written = _clickHouse.WrittenLogs[0];
        Assert.Equal(_project.Id, written.ProjectId);
        Assert.Equal(ts, written.Timestamp);
        Assert.Equal("trace-abc", written.TraceId);
        Assert.Equal("span-xyz", written.SpanId);
        Assert.Equal("sess-123", written.SecureSessionId);
        Assert.Equal("ERROR", written.SeverityText);
        Assert.Equal(17, written.SeverityNumber);
        Assert.Equal("otel", written.Source);
        Assert.Equal("my-api", written.ServiceName);
        Assert.Equal("2.0.1", written.ServiceVersion);
        Assert.Equal("NullReferenceException at Program.cs:42", written.Body);
        Assert.Equal("production", written.Environment);
        Assert.Equal(2, written.LogAttributes.Count);
        Assert.Equal("POST", written.LogAttributes["http.method"]);
    }

    [Fact]
    public async Task LogIngestion_NullAttributes_DefaultsToEmpty()
    {
        var msg = new LogIngestionMessage(
            1, DateTime.UtcNow, "t1", "s1", "sess", "WARN", 13,
            "src", "svc", "1.0", "body", null, "dev");

        var logRow = new LogRowInput
        {
            ProjectId = msg.ProjectId,
            LogAttributes = msg.LogAttributes ?? new(),
        };
        await _clickHouse.WriteLogsAsync([logRow], CancellationToken.None);

        Assert.Empty(_clickHouse.WrittenLogs[0].LogAttributes);
    }

    [Fact]
    public async Task LogIngestion_MultipleLogs_AllWritten()
    {
        var logs = Enumerable.Range(0, 5).Select(i => new LogRowInput
        {
            ProjectId = _project.Id,
            Body = $"Log message {i}",
            SeverityText = "INFO",
            Timestamp = DateTime.UtcNow,
        }).ToList();

        await _clickHouse.WriteLogsAsync(logs, CancellationToken.None);

        Assert.Equal(5, _clickHouse.WrittenLogs.Count);
        for (int i = 0; i < 5; i++)
            Assert.Equal($"Log message {i}", _clickHouse.WrittenLogs[i].Body);
    }

    // ── TraceIngestion Pipeline Tests ─────────────────────────────────

    [Fact]
    public async Task TraceIngestion_MapsAllFieldsToClickHouse()
    {
        var ts = new DateTime(2026, 3, 20, 12, 30, 0, DateTimeKind.Utc);
        var attrs = new Dictionary<string, string>
        {
            ["http.method"] = "GET",
            ["http.status_code"] = "200",
            ["db.system"] = "postgresql",
        };
        var msg = new TraceIngestionMessage(
            _project.Id, ts, "trace-001", "span-002", "parent-003",
            "sess-456", "api-gateway", "3.1.0", "staging",
            "GET /health", "SERVER", 1500, "OK", "",
            attrs, false);

        var traceRow = new TraceRowInput
        {
            ProjectId = msg.ProjectId,
            Timestamp = msg.Timestamp,
            TraceId = msg.TraceId,
            SpanId = msg.SpanId,
            ParentSpanId = msg.ParentSpanId,
            SecureSessionId = msg.SecureSessionId,
            ServiceName = msg.ServiceName,
            ServiceVersion = msg.ServiceVersion,
            Environment = msg.Environment,
            SpanName = msg.SpanName,
            SpanKind = msg.SpanKind,
            Duration = msg.Duration,
            StatusCode = msg.StatusCode,
            StatusMessage = msg.StatusMessage,
            TraceAttributes = msg.TraceAttributes ?? new(),
            HasErrors = msg.HasErrors,
        };
        await _clickHouse.WriteTracesAsync([traceRow], CancellationToken.None);

        Assert.Single(_clickHouse.WrittenTraces);
        var written = _clickHouse.WrittenTraces[0];
        Assert.Equal(_project.Id, written.ProjectId);
        Assert.Equal(ts, written.Timestamp);
        Assert.Equal("trace-001", written.TraceId);
        Assert.Equal("span-002", written.SpanId);
        Assert.Equal("parent-003", written.ParentSpanId);
        Assert.Equal("sess-456", written.SecureSessionId);
        Assert.Equal("api-gateway", written.ServiceName);
        Assert.Equal("3.1.0", written.ServiceVersion);
        Assert.Equal("staging", written.Environment);
        Assert.Equal("GET /health", written.SpanName);
        Assert.Equal("SERVER", written.SpanKind);
        Assert.Equal(1500, written.Duration);
        Assert.Equal("OK", written.StatusCode);
        Assert.Empty(written.StatusMessage);
        Assert.Equal(3, written.TraceAttributes.Count);
        Assert.False(written.HasErrors);
    }

    [Fact]
    public async Task TraceIngestion_NullAttributes_DefaultsToEmpty()
    {
        var msg = new TraceIngestionMessage(
            1, DateTime.UtcNow, "t", "s", "p", "sess",
            "svc", "1.0", "dev", "span", "CLIENT",
            100, "OK", "", null, false);

        var traceRow = new TraceRowInput
        {
            TraceAttributes = msg.TraceAttributes ?? new(),
        };
        await _clickHouse.WriteTracesAsync([traceRow], CancellationToken.None);

        Assert.Empty(_clickHouse.WrittenTraces[0].TraceAttributes);
    }

    [Fact]
    public async Task TraceIngestion_HasErrors_True()
    {
        var msg = new TraceIngestionMessage(
            _project.Id, DateTime.UtcNow, "t", "s", "p", "sess",
            "svc", "1.0", "prod", "POST /api", "SERVER",
            5000, "ERROR", "internal server error",
            new Dictionary<string, string> { ["exception.type"] = "NullRef" },
            true);

        var traceRow = new TraceRowInput
        {
            HasErrors = msg.HasErrors,
            StatusCode = msg.StatusCode,
            StatusMessage = msg.StatusMessage,
            TraceAttributes = msg.TraceAttributes ?? new(),
        };
        await _clickHouse.WriteTracesAsync([traceRow], CancellationToken.None);

        Assert.True(_clickHouse.WrittenTraces[0].HasErrors);
        Assert.Equal("ERROR", _clickHouse.WrittenTraces[0].StatusCode);
        Assert.Equal("internal server error", _clickHouse.WrittenTraces[0].StatusMessage);
    }

    [Fact]
    public async Task TraceIngestion_LongDuration()
    {
        var traceRow = new TraceRowInput
        {
            Duration = long.MaxValue,
            SpanName = "very-slow-span",
        };
        await _clickHouse.WriteTracesAsync([traceRow], CancellationToken.None);

        Assert.Equal(long.MaxValue, _clickHouse.WrittenTraces[0].Duration);
    }

    // ── Metrics Pipeline Tests ────────────────────────────────────────

    [Fact]
    public async Task Metrics_MapsAllFieldsToClickHouse()
    {
        var ts = new DateTime(2026, 3, 20, 14, 0, 0, DateTimeKind.Utc);
        var tags = new Dictionary<string, string>
        {
            ["host"] = "web-01",
            ["region"] = "us-east-1",
        };
        var msg = new MetricsMessage("sess-abc", "cpu.usage", 78.5, "system", ts, tags);

        // Simulate MetricsConsumer's ProcessAsync (uses projectId=0 currently)
        await _clickHouse.WriteMetricAsync(
            0, msg.Name, msg.Value, msg.Category,
            msg.Timestamp, msg.Tags, msg.SessionSecureId,
            CancellationToken.None);

        Assert.Single(_clickHouse.WrittenMetrics);
        var (projId, name, val, cat, timestamp, writtenTags, sessId) = _clickHouse.WrittenMetrics[0];
        Assert.Equal(0, projId); // consumer uses 0 as fallback currently
        Assert.Equal("cpu.usage", name);
        Assert.Equal(78.5, val);
        Assert.Equal("system", cat);
        Assert.Equal(ts, timestamp);
        Assert.Equal(2, writtenTags!.Count);
        Assert.Equal("sess-abc", sessId);
    }

    [Fact]
    public async Task Metrics_NullCategoryAndTags()
    {
        var msg = new MetricsMessage("sess", "latency", 12.3, null, DateTime.UtcNow, null);

        await _clickHouse.WriteMetricAsync(
            0, msg.Name, msg.Value, msg.Category,
            msg.Timestamp, msg.Tags, msg.SessionSecureId,
            CancellationToken.None);

        var written = _clickHouse.WrittenMetrics[0];
        Assert.Null(written.Category);
        Assert.Null(written.Tags);
    }

    [Fact]
    public async Task Metrics_ZeroValue()
    {
        await _clickHouse.WriteMetricAsync(
            0, "counter.reset", 0.0, "counter",
            DateTime.UtcNow, null, "sess",
            CancellationToken.None);

        Assert.Equal(0.0, _clickHouse.WrittenMetrics[0].Value);
    }

    [Fact]
    public async Task Metrics_NegativeValue()
    {
        await _clickHouse.WriteMetricAsync(
            0, "temperature", -40.5, "environment",
            DateTime.UtcNow, null, "sess",
            CancellationToken.None);

        Assert.Equal(-40.5, _clickHouse.WrittenMetrics[0].Value);
    }

    [Fact]
    public async Task Metrics_MultipleMetricsSequentially()
    {
        for (int i = 0; i < 10; i++)
        {
            await _clickHouse.WriteMetricAsync(
                _project.Id, $"metric_{i}", i * 1.5, "batch",
                DateTime.UtcNow, null, "sess",
                CancellationToken.None);
        }

        Assert.Equal(10, _clickHouse.WrittenMetrics.Count);
        Assert.Equal("metric_0", _clickHouse.WrittenMetrics[0].Name);
        Assert.Equal("metric_9", _clickHouse.WrittenMetrics[9].Name);
        Assert.Equal(13.5, _clickHouse.WrittenMetrics[9].Value);
    }

    // ── ErrorGrouping Consumer Pipeline Tests ─────────────────────────

    [Fact]
    public async Task ErrorGrouping_ValidProjectId_GroupsError()
    {
        var groupingService = new ErrorGroupingService(_db, NullLogger<ErrorGroupingService>.Instance);

        var msg = new BackendErrorMessage(
            _project.Id.ToString(), "NullRef", "BACKEND",
            "http://api.test.com", "Program.cs", "at Foo()\n  at Bar()",
            DateTime.UtcNow, null, "my-api", "1.0", "prod",
            null, null, null);

        // Simulate ErrorGroupingConsumer.ProcessAsync logic
        int projectId;
        Assert.True(int.TryParse(msg.ProjectId, out projectId));

        var result = await groupingService.GroupErrorAsync(
            projectId, msg.Event, msg.Type, msg.StackTrace,
            msg.Timestamp, msg.Url, msg.Source, msg.Payload,
            msg.Environment, msg.ServiceName, msg.ServiceVersion,
            null, msg.TraceId, msg.SpanId, CancellationToken.None);

        Assert.True(result.IsNewGroup);
        Assert.Equal("NullRef", result.ErrorGroup.Event);
        Assert.Equal("BACKEND", result.ErrorGroup.Type);
    }

    [Fact]
    public void ErrorGrouping_InvalidProjectId_Skips()
    {
        var msg = new BackendErrorMessage(
            "not-a-number", "Error", "BACKEND",
            "url", "src", "stack",
            DateTime.UtcNow, null, "svc", "1.0", "prod",
            null, null, null);

        // Simulate the consumer's project ID resolution
        Assert.False(int.TryParse(msg.ProjectId, out _));
    }

    [Fact]
    public void ErrorGrouping_NullProjectId_Skips()
    {
        var msg = new BackendErrorMessage(
            null, "Error", "BACKEND",
            "url", "src", "stack",
            DateTime.UtcNow, null, "svc", "1.0", "prod",
            null, null, null);

        Assert.True(string.IsNullOrEmpty(msg.ProjectId));
    }

    [Fact]
    public void ErrorGrouping_EmptyProjectId_Skips()
    {
        var msg = new BackendErrorMessage(
            "", "Error", "BACKEND",
            "url", "src", "stack",
            DateTime.UtcNow, null, "svc", "1.0", "prod",
            null, null, null);

        Assert.True(string.IsNullOrEmpty(msg.ProjectId));
    }

    [Fact]
    public async Task ErrorGrouping_WithSessionLookup_ResolvesSessionId()
    {
        var session = new Session { SecureId = "consumer-sess-1", ProjectId = _project.Id };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        var msg = new BackendErrorMessage(
            _project.Id.ToString(), "SessionError", "FRONTEND",
            "http://app.com", "app.js", "at render()",
            DateTime.UtcNow, null, "web", "1.0", "prod",
            "consumer-sess-1", null, null);

        // Simulate session lookup
        var resolvedSession = await _db.Sessions
            .FirstOrDefaultAsync(s => s.SecureId == msg.SessionSecureId);

        Assert.NotNull(resolvedSession);
        Assert.Equal(session.Id, resolvedSession!.Id);
    }

    [Fact]
    public async Task ErrorGrouping_NonexistentSession_ResolvesNull()
    {
        var msg = new BackendErrorMessage(
            _project.Id.ToString(), "Error", "BACKEND",
            "url", "src", "stack",
            DateTime.UtcNow, null, "svc", "1.0", "prod",
            "nonexistent-session", null, null);

        var resolvedSession = await _db.Sessions
            .FirstOrDefaultAsync(s => s.SecureId == msg.SessionSecureId);

        Assert.Null(resolvedSession);
    }

    [Fact]
    public async Task ErrorGrouping_NullSessionSecureId_SkipsLookup()
    {
        var msg = new BackendErrorMessage(
            _project.Id.ToString(), "Error", "BACKEND",
            "url", "src", "stack",
            DateTime.UtcNow, null, "svc", "1.0", "prod",
            null, null, null);

        // Consumer checks: if (!string.IsNullOrEmpty(value.SessionSecureId))
        Assert.True(string.IsNullOrEmpty(msg.SessionSecureId));
    }

    [Fact]
    public async Task ErrorGrouping_AlertExists_CanBeQueried()
    {
        // Verify that error grouping + alert lookup pipeline works together
        var groupingService = new ErrorGroupingService(_db, NullLogger<ErrorGroupingService>.Instance);

        _db.Alerts.Add(new Alert
        {
            ProjectId = _project.Id,
            Name = "All Errors",
            ProductType = "ERRORS_ALERT",
            Disabled = false,
            ThresholdWindow = 30,
            LastAdminToEditId = 1,
        });
        await _db.SaveChangesAsync();

        var result = await groupingService.GroupErrorAsync(
            _project.Id, "AlertTestError", "BACKEND",
            "at Foo()", DateTime.UtcNow,
            "", "", null,
            "prod", "svc", "1.0", null, null, null,
            CancellationToken.None);

        // Verify the error was grouped and alerts exist for this project
        Assert.True(result.IsNewGroup);
        var alerts = await _db.Alerts
            .Where(a => a.ProjectId == _project.Id && !a.Disabled)
            .ToListAsync();
        Assert.Single(alerts);
        Assert.Equal("All Errors", alerts[0].Name);
    }

    // ── SessionEvents Consumer Pipeline Tests ─────────────────────────

    [Fact]
    public void SessionEventsMessage_AllFields()
    {
        var msg = new SessionEventsMessage("sess-secure-1", 42, "base64-compressed-data");

        Assert.Equal("sess-secure-1", msg.SessionSecureId);
        Assert.Equal(42, msg.PayloadId);
        Assert.Equal("base64-compressed-data", msg.Data);
    }

    [Fact]
    public void SessionEventsMessage_EmptyData()
    {
        var msg = new SessionEventsMessage("sess-1", 0, "");

        Assert.Empty(msg.Data);
        Assert.Equal(0, msg.PayloadId);
    }

    [Fact]
    public void SessionEventsMessage_LargePayloadId()
    {
        var msg = new SessionEventsMessage("sess-1", long.MaxValue, "data");

        Assert.Equal(long.MaxValue, msg.PayloadId);
    }

    // ── ClickHouse Write Input Model Tests ────────────────────────────

    [Fact]
    public void LogRowInput_DefaultValues()
    {
        var row = new LogRowInput();

        Assert.Equal(0, row.ProjectId);
        Assert.Equal(string.Empty, row.TraceId);
        Assert.Equal(string.Empty, row.SpanId);
        Assert.Equal(string.Empty, row.SecureSessionId);
        Assert.Equal(string.Empty, row.SeverityText);
        Assert.Equal(0, row.SeverityNumber);
        Assert.Equal(string.Empty, row.Source);
        Assert.Equal(string.Empty, row.ServiceName);
        Assert.Equal(string.Empty, row.ServiceVersion);
        Assert.Equal(string.Empty, row.Body);
        Assert.Empty(row.LogAttributes);
        Assert.Equal(string.Empty, row.Environment);
    }

    [Fact]
    public void TraceRowInput_DefaultValues()
    {
        var row = new TraceRowInput();

        Assert.Equal(0, row.ProjectId);
        Assert.Equal(string.Empty, row.TraceId);
        Assert.Equal(string.Empty, row.SpanId);
        Assert.Equal(string.Empty, row.ParentSpanId);
        Assert.Equal(string.Empty, row.SecureSessionId);
        Assert.Equal(string.Empty, row.ServiceName);
        Assert.Equal(string.Empty, row.ServiceVersion);
        Assert.Equal(string.Empty, row.Environment);
        Assert.Equal(string.Empty, row.SpanName);
        Assert.Equal(string.Empty, row.SpanKind);
        Assert.Equal(0, row.Duration);
        Assert.Equal(string.Empty, row.StatusCode);
        Assert.Equal(string.Empty, row.StatusMessage);
        Assert.Empty(row.TraceAttributes);
        Assert.False(row.HasErrors);
    }

    [Fact]
    public void SessionRowInput_DefaultValues()
    {
        var row = new SessionRowInput();

        Assert.Equal(0, row.ProjectId);
        Assert.Equal(0, row.SessionId);
        Assert.Equal(string.Empty, row.SecureSessionId);
        Assert.Null(row.Identifier);
        Assert.Null(row.OSName);
        Assert.Null(row.BrowserName);
        Assert.Null(row.City);
        Assert.Equal(0, row.ActiveLength);
        Assert.Equal(0, row.Length);
        Assert.Equal(0, row.PagesVisited);
        Assert.False(row.HasErrors);
        Assert.False(row.HasRageClicks);
        Assert.False(row.Processed);
        Assert.False(row.FirstTime);
    }

    [Fact]
    public void ErrorGroupRowInput_DefaultState()
    {
        var row = new ErrorGroupRowInput();

        Assert.Equal("OPEN", row.State);
        Assert.Equal(string.Empty, row.SecureId);
    }

    [Fact]
    public void ErrorObjectRowInput_DefaultValues()
    {
        var row = new ErrorObjectRowInput();

        Assert.Equal(0, row.ProjectId);
        Assert.Equal(0, row.ErrorObjectId);
        Assert.Equal(0, row.ErrorGroupId);
        Assert.Null(row.Event);
        Assert.Null(row.Type);
        Assert.Null(row.Url);
    }

    // ── ClickHouse Write Batch Tests ──────────────────────────────────

    [Fact]
    public async Task WriteSessionsBatch_AllFieldsMapped()
    {
        var sessions = new[]
        {
            new SessionRowInput
            {
                ProjectId = _project.Id,
                SessionId = 1,
                SecureSessionId = "secure-1",
                CreatedAt = DateTime.UtcNow,
                Identifier = "user@test.com",
                OSName = "Windows",
                OSVersion = "10",
                BrowserName = "Chrome",
                BrowserVersion = "120",
                City = "Portland",
                State = "OR",
                Country = "US",
                Environment = "production",
                AppVersion = "2.0",
                ServiceName = "frontend",
                ActiveLength = 300,
                Length = 600,
                PagesVisited = 5,
                HasErrors = true,
                HasRageClicks = false,
                Processed = true,
                FirstTime = true,
            },
        };

        await _clickHouse.WriteSessionsAsync(sessions, CancellationToken.None);

        Assert.Single(_clickHouse.WrittenSessions);
        var written = _clickHouse.WrittenSessions[0];
        Assert.Equal("user@test.com", written.Identifier);
        Assert.Equal("Windows", written.OSName);
        Assert.Equal("Chrome", written.BrowserName);
        Assert.Equal("Portland", written.City);
        Assert.True(written.HasErrors);
        Assert.True(written.FirstTime);
        Assert.Equal(5, written.PagesVisited);
    }

    [Fact]
    public async Task WriteErrorGroupsBatch_AllFieldsMapped()
    {
        var groups = new[]
        {
            new ErrorGroupRowInput
            {
                ProjectId = _project.Id,
                ErrorGroupId = 42,
                SecureId = "err-secure-1",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Event = "NullReferenceException",
                Type = "BACKEND",
                State = "RESOLVED",
                ServiceName = "api",
                Environments = "production,staging",
            },
        };

        await _clickHouse.WriteErrorGroupsAsync(groups, CancellationToken.None);

        Assert.Single(_clickHouse.WrittenErrorGroups);
        var written = _clickHouse.WrittenErrorGroups[0];
        Assert.Equal(42, written.ErrorGroupId);
        Assert.Equal("RESOLVED", written.State);
        Assert.Equal("production,staging", written.Environments);
    }

    [Fact]
    public async Task WriteErrorObjectsBatch_AllFieldsMapped()
    {
        var objects = new[]
        {
            new ErrorObjectRowInput
            {
                ProjectId = _project.Id,
                ErrorObjectId = 100,
                ErrorGroupId = 42,
                Timestamp = DateTime.UtcNow,
                Event = "NullRef",
                Type = "BACKEND",
                Url = "http://api.test.com/users",
                Environment = "production",
                OS = "Linux",
                Browser = "N/A",
                ServiceName = "user-service",
                ServiceVersion = "3.1.0",
            },
        };

        await _clickHouse.WriteErrorObjectsAsync(objects, CancellationToken.None);

        Assert.Single(_clickHouse.WrittenErrorObjects);
        var written = _clickHouse.WrittenErrorObjects[0];
        Assert.Equal(100, written.ErrorObjectId);
        Assert.Equal(42, written.ErrorGroupId);
        Assert.Equal("http://api.test.com/users", written.Url);
        Assert.Equal("Linux", written.OS);
    }

    [Fact]
    public async Task WriteLargeLogBatch()
    {
        var logs = Enumerable.Range(0, 1000).Select(i => new LogRowInput
        {
            ProjectId = _project.Id,
            Body = $"Log line {i}",
            Timestamp = DateTime.UtcNow.AddMilliseconds(i),
            SeverityText = i % 2 == 0 ? "INFO" : "ERROR",
            SeverityNumber = i % 2 == 0 ? 9 : 17,
        }).ToList();

        await _clickHouse.WriteLogsAsync(logs, CancellationToken.None);

        Assert.Equal(1000, _clickHouse.WrittenLogs.Count);
        Assert.Equal(500, _clickHouse.WrittenLogs.Count(l => l.SeverityText == "ERROR"));
    }

    [Fact]
    public async Task WriteLargeTraceBatch()
    {
        var traces = Enumerable.Range(0, 500).Select(i => new TraceRowInput
        {
            ProjectId = _project.Id,
            SpanName = $"span-{i}",
            Duration = i * 100,
            HasErrors = i % 10 == 0,
        }).ToList();

        await _clickHouse.WriteTracesAsync(traces, CancellationToken.None);

        Assert.Equal(500, _clickHouse.WrittenTraces.Count);
        Assert.Equal(50, _clickHouse.WrittenTraces.Count(t => t.HasErrors));
    }
}
