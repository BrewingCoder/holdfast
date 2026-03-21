using HoldFast.Data.ClickHouse;
using HoldFast.Data.ClickHouse.Models;
using HoldFast.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using HoldFast.Shared.Kafka;
using Xunit;

namespace HoldFast.Worker.Tests;

/// <summary>
/// Tests the ProcessAsync logic of each consumer by creating testable subclasses
/// that expose the protected method. Verifies message → ClickHouse mapping
/// through actual DI-resolved IClickHouseService.
/// </summary>
public class ConsumerProcessAsyncTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly RecordingClickHouseService _clickHouse;

    public ConsumerProcessAsyncTests()
    {
        _clickHouse = new RecordingClickHouseService();

        var services = new ServiceCollection();
        services.AddSingleton<IClickHouseService>(_clickHouse);
        _serviceProvider = services.BuildServiceProvider();
    }

    public void Dispose() => _serviceProvider.Dispose();

    private IServiceScopeFactory ScopeFactory => _serviceProvider.GetRequiredService<IServiceScopeFactory>();

    // ══════════════════════════════════════════════════════════════════
    // MetricsConsumer — via testable subclass
    // ══════════════════════════════════════════════════════════════════

    private class TestableMetricsConsumer : MetricsConsumer
    {
        public TestableMetricsConsumer(IServiceScopeFactory sf)
            : base(Options.Create(new KafkaOptions { BootstrapServers = "test:9092" }),
                   sf, NullLogger<MetricsConsumer>.Instance) { }

        public new Task ProcessAsync(string key, MetricsMessage value, CancellationToken ct)
            => base.ProcessAsync(key, value, ct);
    }

    [Fact]
    public async Task MetricsConsumer_WritesMetricToClickHouse()
    {
        var consumer = new TestableMetricsConsumer(ScopeFactory);
        var msg = new MetricsMessage("sess-1", "LCP", 2.5, "web-vital",
            new DateTime(2026, 3, 20, 10, 0, 0, DateTimeKind.Utc),
            new Dictionary<string, string> { ["page"] = "/home" });

        await consumer.ProcessAsync("sess-1", msg, CancellationToken.None);

        Assert.Single(_clickHouse.WrittenMetrics);
        var m = _clickHouse.WrittenMetrics[0];
        Assert.Equal("LCP", m.Name);
        Assert.Equal(2.5, m.Value);
        Assert.Equal("web-vital", m.Category);
        Assert.Equal("sess-1", m.SessionId);
        Assert.Equal("/home", m.Tags!["page"]);
    }

    [Fact]
    public async Task MetricsConsumer_NullTags_PassedThrough()
    {
        var consumer = new TestableMetricsConsumer(ScopeFactory);
        var msg = new MetricsMessage("sess-1", "CLS", 0.1, null, DateTime.UtcNow, null);

        await consumer.ProcessAsync("sess-1", msg, CancellationToken.None);

        Assert.Null(_clickHouse.WrittenMetrics[0].Tags);
        Assert.Null(_clickHouse.WrittenMetrics[0].Category);
    }

    [Fact]
    public async Task MetricsConsumer_ZeroValue()
    {
        var consumer = new TestableMetricsConsumer(ScopeFactory);
        await consumer.ProcessAsync("k", new MetricsMessage("s", "m", 0.0, null, DateTime.UtcNow, null), default);
        Assert.Equal(0.0, _clickHouse.WrittenMetrics[0].Value);
    }

    [Fact]
    public async Task MetricsConsumer_NegativeValue()
    {
        var consumer = new TestableMetricsConsumer(ScopeFactory);
        await consumer.ProcessAsync("k", new MetricsMessage("s", "delta", -42.5, null, DateTime.UtcNow, null), default);
        Assert.Equal(-42.5, _clickHouse.WrittenMetrics[0].Value);
    }

    [Fact]
    public async Task MetricsConsumer_EmptyStringName()
    {
        var consumer = new TestableMetricsConsumer(ScopeFactory);
        await consumer.ProcessAsync("k", new MetricsMessage("s", "", 1.0, null, DateTime.UtcNow, null), default);
        Assert.Equal("", _clickHouse.WrittenMetrics[0].Name);
    }

    [Fact]
    public async Task MetricsConsumer_ManyTags()
    {
        var consumer = new TestableMetricsConsumer(ScopeFactory);
        var tags = Enumerable.Range(0, 20).ToDictionary(i => $"tag{i}", i => $"val{i}");
        await consumer.ProcessAsync("k", new MetricsMessage("s", "m", 1.0, "cat", DateTime.UtcNow, tags), default);
        Assert.Equal(20, _clickHouse.WrittenMetrics[0].Tags!.Count);
    }

    // ══════════════════════════════════════════════════════════════════
    // LogIngestionConsumer — via testable subclass
    // ══════════════════════════════════════════════════════════════════

    private class TestableLogConsumer : LogIngestionConsumer
    {
        public TestableLogConsumer(IServiceScopeFactory sf)
            : base(Options.Create(new KafkaOptions { BootstrapServers = "test:9092" }),
                   sf, NullLogger<LogIngestionConsumer>.Instance) { }

        public new Task ProcessAsync(string key, LogIngestionMessage value, CancellationToken ct)
            => base.ProcessAsync(key, value, ct);
    }

    [Fact]
    public async Task LogConsumer_MapsAllFields()
    {
        var consumer = new TestableLogConsumer(ScopeFactory);
        var ts = new DateTime(2026, 3, 20, 15, 30, 0, DateTimeKind.Utc);
        var msg = new LogIngestionMessage(
            42, ts, "trace-1", "span-1", "sess-1",
            "ERROR", 17, "otel", "my-api", "2.0.0",
            "NullReferenceException",
            new Dictionary<string, string> { ["http.method"] = "POST" },
            "production");

        await consumer.ProcessAsync("trace-1", msg, default);

        Assert.Single(_clickHouse.WrittenLogs);
        var log = _clickHouse.WrittenLogs[0];
        Assert.Equal(42, log.ProjectId);
        Assert.Equal(ts, log.Timestamp);
        Assert.Equal("trace-1", log.TraceId);
        Assert.Equal("span-1", log.SpanId);
        Assert.Equal("sess-1", log.SecureSessionId);
        Assert.Equal("ERROR", log.SeverityText);
        Assert.Equal(17, log.SeverityNumber);
        Assert.Equal("NullReferenceException", log.Body);
        Assert.Equal("production", log.Environment);
        Assert.Equal("POST", log.LogAttributes["http.method"]);
    }

    [Fact]
    public async Task LogConsumer_NullAttributes_DefaultsToEmpty()
    {
        var consumer = new TestableLogConsumer(ScopeFactory);
        var msg = new LogIngestionMessage(1, DateTime.UtcNow, "t", "s", "sess",
            "INFO", 9, "src", "svc", "1.0", "body", null, "dev");

        await consumer.ProcessAsync("t", msg, default);

        Assert.Empty(_clickHouse.WrittenLogs[0].LogAttributes);
    }

    [Fact]
    public async Task LogConsumer_EmptyBody()
    {
        var consumer = new TestableLogConsumer(ScopeFactory);
        var msg = new LogIngestionMessage(1, DateTime.UtcNow, "t", "s", "sess",
            "DEBUG", 5, "src", "svc", "1.0", "", null, "dev");

        await consumer.ProcessAsync("t", msg, default);
        Assert.Empty(_clickHouse.WrittenLogs[0].Body);
    }

    [Fact]
    public async Task LogConsumer_HighSeverityNumber()
    {
        var consumer = new TestableLogConsumer(ScopeFactory);
        var msg = new LogIngestionMessage(1, DateTime.UtcNow, "t", "s", "sess",
            "FATAL", 24, "src", "svc", "1.0", "crash", null, "prod");

        await consumer.ProcessAsync("t", msg, default);
        Assert.Equal(24, _clickHouse.WrittenLogs[0].SeverityNumber);
    }

    // ══════════════════════════════════════════════════════════════════
    // TraceIngestionConsumer — via testable subclass
    // ══════════════════════════════════════════════════════════════════

    private class TestableTraceConsumer : TraceIngestionConsumer
    {
        public TestableTraceConsumer(IServiceScopeFactory sf)
            : base(Options.Create(new KafkaOptions { BootstrapServers = "test:9092" }),
                   sf, NullLogger<TraceIngestionConsumer>.Instance) { }

        public new Task ProcessAsync(string key, TraceIngestionMessage value, CancellationToken ct)
            => base.ProcessAsync(key, value, ct);
    }

    [Fact]
    public async Task TraceConsumer_MapsAllFields()
    {
        var consumer = new TestableTraceConsumer(ScopeFactory);
        var ts = new DateTime(2026, 3, 20, 16, 0, 0, DateTimeKind.Utc);
        var msg = new TraceIngestionMessage(
            42, ts, "trace-abc", "span-001", "parent-000",
            "sess-1", "api-gateway", "3.0.0", "staging",
            "GET /health", "SERVER", 1500, "OK", "",
            new Dictionary<string, string> { ["http.status_code"] = "200" },
            false);

        await consumer.ProcessAsync("trace-abc", msg, default);

        Assert.Single(_clickHouse.WrittenTraces);
        var trace = _clickHouse.WrittenTraces[0];
        Assert.Equal(42, trace.ProjectId);
        Assert.Equal(ts, trace.Timestamp);
        Assert.Equal("trace-abc", trace.TraceId);
        Assert.Equal("span-001", trace.SpanId);
        Assert.Equal("parent-000", trace.ParentSpanId);
        Assert.Equal("api-gateway", trace.ServiceName);
        Assert.Equal("GET /health", trace.SpanName);
        Assert.Equal("SERVER", trace.SpanKind);
        Assert.Equal(1500, trace.Duration);
        Assert.Equal("OK", trace.StatusCode);
        Assert.False(trace.HasErrors);
        Assert.Equal("200", trace.TraceAttributes["http.status_code"]);
    }

    [Fact]
    public async Task TraceConsumer_NullAttributes_DefaultsToEmpty()
    {
        var consumer = new TestableTraceConsumer(ScopeFactory);
        var msg = new TraceIngestionMessage(1, DateTime.UtcNow, "t", "s", "",
            "", "svc", "1.0", "dev", "span", "CLIENT", 100, "OK", "", null, false);

        await consumer.ProcessAsync("t", msg, default);
        Assert.Empty(_clickHouse.WrittenTraces[0].TraceAttributes);
    }

    [Fact]
    public async Task TraceConsumer_HasErrors()
    {
        var consumer = new TestableTraceConsumer(ScopeFactory);
        var msg = new TraceIngestionMessage(1, DateTime.UtcNow, "t", "s", "",
            "", "svc", "1.0", "prod", "span", "SERVER", 5000,
            "ERROR", "Internal error", null, true);

        await consumer.ProcessAsync("t", msg, default);

        Assert.True(_clickHouse.WrittenTraces[0].HasErrors);
        Assert.Equal("ERROR", _clickHouse.WrittenTraces[0].StatusCode);
        Assert.Equal("Internal error", _clickHouse.WrittenTraces[0].StatusMessage);
    }

    [Fact]
    public async Task TraceConsumer_ZeroDuration()
    {
        var consumer = new TestableTraceConsumer(ScopeFactory);
        var msg = new TraceIngestionMessage(1, DateTime.UtcNow, "t", "s", "",
            "", "svc", "1.0", "dev", "instant", "INTERNAL", 0, "OK", "", null, false);

        await consumer.ProcessAsync("t", msg, default);
        Assert.Equal(0, _clickHouse.WrittenTraces[0].Duration);
    }

    [Fact]
    public async Task TraceConsumer_VeryLongDuration()
    {
        var consumer = new TestableTraceConsumer(ScopeFactory);
        var msg = new TraceIngestionMessage(1, DateTime.UtcNow, "t", "s", "",
            "", "svc", "1.0", "prod", "slow", "SERVER", 3_600_000, "OK", "", null, false);

        await consumer.ProcessAsync("t", msg, default);
        Assert.Equal(3_600_000, _clickHouse.WrittenTraces[0].Duration);
    }

    // ══════════════════════════════════════════════════════════════════
    // Message record types — construction and edge cases
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void MetricsMessage_Equality()
    {
        var a = new MetricsMessage("s", "m", 1.0, "c", DateTime.UnixEpoch, null);
        var b = new MetricsMessage("s", "m", 1.0, "c", DateTime.UnixEpoch, null);
        Assert.Equal(a, b);
    }

    [Fact]
    public void LogIngestionMessage_Equality()
    {
        var a = new LogIngestionMessage(1, DateTime.UnixEpoch, "t", "s", "sess",
            "INFO", 9, "src", "svc", "1.0", "body", null, "dev");
        var b = new LogIngestionMessage(1, DateTime.UnixEpoch, "t", "s", "sess",
            "INFO", 9, "src", "svc", "1.0", "body", null, "dev");
        Assert.Equal(a, b);
    }

    [Fact]
    public void TraceIngestionMessage_Equality()
    {
        var a = new TraceIngestionMessage(1, DateTime.UnixEpoch, "t", "s", "",
            "", "svc", "1.0", "dev", "span", "CLIENT", 0, "OK", "", null, false);
        var b = new TraceIngestionMessage(1, DateTime.UnixEpoch, "t", "s", "",
            "", "svc", "1.0", "dev", "span", "CLIENT", 0, "OK", "", null, false);
        Assert.Equal(a, b);
    }

    // ══════════════════════════════════════════════════════════════════
    // Recording ClickHouse fake
    // ══════════════════════════════════════════════════════════════════

    private class RecordingClickHouseService : IClickHouseService
    {
        public List<LogRowInput> WrittenLogs { get; } = [];
        public List<TraceRowInput> WrittenTraces { get; } = [];
        public List<(int ProjectId, string Name, double Value, string? Category, DateTime Timestamp, Dictionary<string, string>? Tags, string? SessionId)> WrittenMetrics { get; } = [];

        public Task WriteLogsAsync(IEnumerable<LogRowInput> logs, CancellationToken ct) { WrittenLogs.AddRange(logs); return Task.CompletedTask; }
        public Task WriteTracesAsync(IEnumerable<TraceRowInput> traces, CancellationToken ct) { WrittenTraces.AddRange(traces); return Task.CompletedTask; }
        public Task WriteMetricAsync(int projectId, string metricName, double metricValue, string? category, DateTime timestamp, Dictionary<string, string>? tags, string? sessionSecureId, CancellationToken ct)
        {
            WrittenMetrics.Add((projectId, metricName, metricValue, category, timestamp, tags, sessionSecureId));
            return Task.CompletedTask;
        }

        public Task WriteSessionsAsync(IEnumerable<SessionRowInput> s, CancellationToken ct) => Task.CompletedTask;
        public Task WriteErrorGroupsAsync(IEnumerable<ErrorGroupRowInput> e, CancellationToken ct) => Task.CompletedTask;
        public Task WriteErrorObjectsAsync(IEnumerable<ErrorObjectRowInput> e, CancellationToken ct) => Task.CompletedTask;
        public Task<LogConnection> ReadLogsAsync(int p, QueryInput q, ClickHousePagination pag, CancellationToken ct) => Task.FromResult(new LogConnection());
        public Task<List<HistogramBucket>> ReadLogsHistogramAsync(int p, QueryInput q, CancellationToken ct) => Task.FromResult(new List<HistogramBucket>());
        public Task<List<string>> GetLogKeysAsync(int p, QueryInput q, CancellationToken ct) => Task.FromResult(new List<string>());
        public Task<List<string>> GetLogKeyValuesAsync(int p, string k, QueryInput q, CancellationToken ct) => Task.FromResult(new List<string>());
        public Task<TraceConnection> ReadTracesAsync(int p, QueryInput q, ClickHousePagination pag, bool o, CancellationToken ct) => Task.FromResult(new TraceConnection());
        public Task<List<HistogramBucket>> ReadTracesHistogramAsync(int p, QueryInput q, CancellationToken ct) => Task.FromResult(new List<HistogramBucket>());
        public Task<List<string>> GetTraceKeysAsync(int p, QueryInput q, CancellationToken ct) => Task.FromResult(new List<string>());
        public Task<List<string>> GetTraceKeyValuesAsync(int p, string k, QueryInput q, CancellationToken ct) => Task.FromResult(new List<string>());
        public Task<List<HistogramBucket>> ReadSessionsHistogramAsync(int p, QueryInput q, CancellationToken ct) => Task.FromResult(new List<HistogramBucket>());
        public Task<(List<int> Ids, long Total)> QuerySessionIdsAsync(int p, QueryInput q, int c, int pg, string? sf, bool sd, CancellationToken ct) => Task.FromResult((new List<int>(), 0L));
        public Task<(List<int> Ids, long Total)> QueryErrorGroupIdsAsync(int p, QueryInput q, int c, int pg, CancellationToken ct) => Task.FromResult((new List<int>(), 0L));
        public Task<List<HistogramBucket>> ReadErrorObjectsHistogramAsync(int p, QueryInput q, CancellationToken ct) => Task.FromResult(new List<HistogramBucket>());
        public Task<MetricsBuckets> ReadMetricsAsync(int p, QueryInput q, string b, List<string>? g, string a, string? col, CancellationToken ct) => Task.FromResult(new MetricsBuckets());
        public Task<List<QueryKey>> GetSessionsKeysAsync(int p, DateTime s, DateTime e, string? q, CancellationToken ct) => Task.FromResult(new List<QueryKey>());
        public Task<List<string>> GetSessionsKeyValuesAsync(int p, string k, DateTime s, DateTime e, string? q, int? c, CancellationToken ct) => Task.FromResult(new List<string>());
        public Task<List<QueryKey>> GetErrorsKeysAsync(int p, DateTime s, DateTime e, string? q, CancellationToken ct) => Task.FromResult(new List<QueryKey>());
        public Task<List<string>> GetErrorsKeyValuesAsync(int p, string k, DateTime s, DateTime e, string? q, int? c, CancellationToken ct) => Task.FromResult(new List<string>());
        public Task<List<QueryKey>> GetEventsKeysAsync(int p, DateTime s, DateTime e, string? q, string? en, CancellationToken ct) => Task.FromResult(new List<QueryKey>());
        public Task<List<string>> GetEventsKeyValuesAsync(int p, string k, DateTime s, DateTime e, string? q, int? c, string? en, CancellationToken ct) => Task.FromResult(new List<string>());
    }
}
