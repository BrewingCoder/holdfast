using HoldFast.GraphQL.Public;
using HoldFast.GraphQL.Public.InputTypes;
using Xunit;

namespace HoldFast.GraphQL.Tests;

/// <summary>
/// Tests for Kafka-dependent public mutations: PushSessionEvents,
/// PushBackendPayload, PushMetrics. Uses a recording fake producer.
/// </summary>
public class PublicMutationKafkaTests
{
    private readonly PublicMutation _mutation = new();

    // ── Fake Producer ────────────────────────────────────────────────

    private class FakeKafkaProducer : IKafkaProducer
    {
        public List<(string SessionSecureId, long PayloadId, string Data)> SessionEvents { get; } = [];
        public List<(string? ProjectId, BackendErrorObjectInput Error)> BackendErrors { get; } = [];
        public List<MetricInput> Metrics { get; } = [];
        public List<LogInput> Logs { get; } = [];
        public List<TraceInput> Traces { get; } = [];

        public Task ProduceSessionEventsAsync(string sessionSecureId, long payloadId, string data, CancellationToken ct)
        {
            SessionEvents.Add((sessionSecureId, payloadId, data));
            return Task.CompletedTask;
        }

        public Task ProduceBackendErrorAsync(string? projectId, BackendErrorObjectInput error, CancellationToken ct)
        {
            BackendErrors.Add((projectId, error));
            return Task.CompletedTask;
        }

        public Task ProduceMetricAsync(MetricInput metric, CancellationToken ct)
        {
            Metrics.Add(metric);
            return Task.CompletedTask;
        }

        public Task ProduceLogAsync(LogInput log, CancellationToken ct)
        {
            Logs.Add(log);
            return Task.CompletedTask;
        }

        public Task ProduceTraceAsync(TraceInput trace, CancellationToken ct)
        {
            Traces.Add(trace);
            return Task.CompletedTask;
        }
    }

    // ── PushSessionEvents ────────────────────────────────────────────

    [Fact]
    public async Task PushSessionEvents_ProducesToKafka()
    {
        var kafka = new FakeKafkaProducer();

        var result = await _mutation.PushSessionEvents(
            "sess-abc", 42, "compressed-data",
            kafka, CancellationToken.None);

        Assert.True(result);
        Assert.Single(kafka.SessionEvents);
        Assert.Equal("sess-abc", kafka.SessionEvents[0].SessionSecureId);
        Assert.Equal(42, kafka.SessionEvents[0].PayloadId);
        Assert.Equal("compressed-data", kafka.SessionEvents[0].Data);
    }

    [Fact]
    public async Task PushSessionEvents_EmptyData()
    {
        var kafka = new FakeKafkaProducer();

        var result = await _mutation.PushSessionEvents(
            "sess-1", 0, "",
            kafka, CancellationToken.None);

        Assert.True(result);
        Assert.Single(kafka.SessionEvents);
        Assert.Empty(kafka.SessionEvents[0].Data);
    }

    [Fact]
    public async Task PushSessionEvents_LargePayloadId()
    {
        var kafka = new FakeKafkaProducer();

        await _mutation.PushSessionEvents(
            "sess-1", long.MaxValue, "data",
            kafka, CancellationToken.None);

        Assert.Equal(long.MaxValue, kafka.SessionEvents[0].PayloadId);
    }

    // ── PushBackendPayload ───────────────────────────────────────────

    [Fact]
    public async Task PushBackendPayload_ProducesAllErrors()
    {
        var kafka = new FakeKafkaProducer();
        var svc = new ServiceInput("api-server", "1.0");

        var errors = new List<BackendErrorObjectInput>
        {
            new(null, null, null, null, null, "NullRef", "System.NullReferenceException",
                "/api", "backend", "at MyApp.Service()", DateTime.UtcNow, null, svc, "prod"),
            new(null, null, null, null, null, "DivZero", "System.DivideByZeroException",
                "/calc", "backend", "at MyApp.Calculator()", DateTime.UtcNow, null, svc, "prod"),
        };

        var result = await _mutation.PushBackendPayload(
            "123", errors,
            kafka, CancellationToken.None);

        Assert.True(result);
        Assert.Equal(2, kafka.BackendErrors.Count);
        Assert.Equal("123", kafka.BackendErrors[0].ProjectId);
        Assert.Equal("NullRef", kafka.BackendErrors[0].Error.Event);
        Assert.Equal("DivZero", kafka.BackendErrors[1].Error.Event);
    }

    [Fact]
    public async Task PushBackendPayload_EmptyErrors_ReturnsTrue()
    {
        var kafka = new FakeKafkaProducer();

        var result = await _mutation.PushBackendPayload(
            "123", [],
            kafka, CancellationToken.None);

        Assert.True(result);
        Assert.Empty(kafka.BackendErrors);
    }

    [Fact]
    public async Task PushBackendPayload_NullProjectId()
    {
        var kafka = new FakeKafkaProducer();
        var svc = new ServiceInput("svc", "1.0");

        var errors = new List<BackendErrorObjectInput>
        {
            new(null, null, null, null, null, "Error", "Error",
                "", "", "", DateTime.UtcNow, null, svc, "dev"),
        };

        var result = await _mutation.PushBackendPayload(
            null, errors,
            kafka, CancellationToken.None);

        Assert.True(result);
        Assert.Null(kafka.BackendErrors[0].ProjectId);
    }

    [Fact]
    public async Task PushBackendPayload_PreservesTraceContext()
    {
        var kafka = new FakeKafkaProducer();
        var svc = new ServiceInput("svc", "1.0");

        var errors = new List<BackendErrorObjectInput>
        {
            new("sess-1", "req-1", "trace-abc", "span-xyz", "cursor-1",
                "Error", "AppError", "/api", "backend", "stack",
                DateTime.UtcNow, "{\"key\":\"val\"}", svc, "prod"),
        };

        await _mutation.PushBackendPayload("42", errors, kafka, CancellationToken.None);

        var produced = kafka.BackendErrors[0].Error;
        Assert.Equal("sess-1", produced.SessionSecureId);
        Assert.Equal("req-1", produced.RequestId);
        Assert.Equal("trace-abc", produced.TraceId);
        Assert.Equal("span-xyz", produced.SpanId);
        Assert.Equal("cursor-1", produced.LogCursor);
        Assert.Equal("{\"key\":\"val\"}", produced.Payload);
    }

    // ── PushMetrics ──────────────────────────────────────────────────

    [Fact]
    public async Task PushMetrics_ProducesAllMetrics()
    {
        var kafka = new FakeKafkaProducer();

        var metrics = new List<MetricInput>
        {
            new("sess-1", null, null, null, null, "LCP", 2.5, "web-vital", DateTime.UtcNow, null),
            new("sess-1", null, null, null, null, "FID", 100.0, "web-vital", DateTime.UtcNow, null),
            new("sess-1", null, null, null, null, "CLS", 0.1, "web-vital", DateTime.UtcNow, null),
        };

        var count = await _mutation.PushMetrics(
            metrics, kafka, CancellationToken.None);

        Assert.Equal(3, count);
        Assert.Equal(3, kafka.Metrics.Count);
        Assert.Equal("LCP", kafka.Metrics[0].Name);
        Assert.Equal("FID", kafka.Metrics[1].Name);
        Assert.Equal("CLS", kafka.Metrics[2].Name);
    }

    [Fact]
    public async Task PushMetrics_EmptyList_ReturnsZero()
    {
        var kafka = new FakeKafkaProducer();

        var count = await _mutation.PushMetrics(
            [], kafka, CancellationToken.None);

        Assert.Equal(0, count);
        Assert.Empty(kafka.Metrics);
    }

    [Fact]
    public async Task PushMetrics_WithTags()
    {
        var kafka = new FakeKafkaProducer();

        var tags = new List<MetricTag>
        {
            new("page", "/home"),
            new("env", "prod"),
        };

        var metrics = new List<MetricInput>
        {
            new("sess-1", "span-1", "parent-1", "trace-1", "grp", "custom_metric", 42.0, "custom", DateTime.UtcNow, tags),
        };

        var count = await _mutation.PushMetrics(
            metrics, kafka, CancellationToken.None);

        Assert.Equal(1, count);
        Assert.NotNull(kafka.Metrics[0].Tags);
        Assert.Equal(2, kafka.Metrics[0].Tags!.Count);
    }

    [Fact]
    public async Task PushMetrics_PreservesSpanAndTraceContext()
    {
        var kafka = new FakeKafkaProducer();
        var ts = new DateTime(2026, 3, 20, 12, 0, 0, DateTimeKind.Utc);

        var metrics = new List<MetricInput>
        {
            new("sess-1", "span-abc", "parent-xyz", "trace-123", "mygroup",
                "metric", 1.0, "cat", ts, null),
        };

        await _mutation.PushMetrics(metrics, kafka, CancellationToken.None);

        var produced = kafka.Metrics[0];
        Assert.Equal("span-abc", produced.SpanId);
        Assert.Equal("parent-xyz", produced.ParentSpanId);
        Assert.Equal("trace-123", produced.TraceId);
        Assert.Equal("mygroup", produced.Group);
        Assert.Equal(ts, produced.Timestamp);
    }
}
