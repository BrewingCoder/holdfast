using System.Text.Json;
using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.GraphQL.Public;
using HoldFast.GraphQL.Public.InputTypes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HoldFast.GraphQL.Tests;

/// <summary>
/// Tests for PublicMutation Kafka-producing mutations using a stub producer.
/// Verifies data flows correctly to Kafka topics.
/// </summary>
public class PublicMutationKafkaTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly PublicMutation _mutation;
    private readonly StubKafkaProducer _kafka;
    private readonly Project _project;

    public PublicMutationKafkaTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options;
        _db = new HoldFastDbContext(options);
        _db.Database.EnsureCreated();

        var workspace = new Workspace { Name = "WS", PlanTier = "Enterprise" };
        _db.Workspaces.Add(workspace);
        _db.SaveChanges();

        _project = new Project { Name = "Proj", WorkspaceId = workspace.Id };
        _db.Projects.Add(_project);
        _db.SaveChanges();

        _db.Sessions.Add(new Session { ProjectId = _project.Id, SecureId = "kafka-session-1" });
        _db.SaveChanges();

        _mutation = new PublicMutation();
        _kafka = new StubKafkaProducer();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    // ── PushSessionEvents ──────────────────────────────────────────────

    [Fact]
    public async Task PushSessionEvents_ProducesToKafka()
    {
        await _mutation.PushSessionEvents("kafka-session-1", 1, "compressed-data", _kafka, CancellationToken.None);

        Assert.Equal(1, _kafka.SessionEventsCount);
        Assert.Equal("kafka-session-1", _kafka.LastSessionSecureId);
        Assert.Equal("compressed-data", _kafka.LastSessionData);
    }

    [Fact]
    public async Task PushSessionEvents_LargePayloadId()
    {
        await _mutation.PushSessionEvents("kafka-session-1", long.MaxValue, "data", _kafka, CancellationToken.None);
        Assert.Equal(long.MaxValue, _kafka.LastPayloadId);
    }

    // ── PushBackendPayload ─────────────────────────────────────────────

    [Fact]
    public async Task PushBackendPayload_ProducesForEachError()
    {
        var svc = new ServiceInput("test-svc", "1.0");
        var errors = new List<BackendErrorObjectInput>
        {
            new("sess-1", null, null, null, null, "Error 1", "BACKEND", "http://test.com", "api.go", "[]", DateTime.UtcNow, null, svc, "prod"),
            new("sess-1", null, null, null, null, "Error 2", "BACKEND", "http://test.com", "api.go", "[]", DateTime.UtcNow, null, svc, "prod"),
        };

        await _mutation.PushBackendPayload(_project.Id.ToString(), errors, _kafka, CancellationToken.None);

        Assert.Equal(2, _kafka.BackendErrorCount);
    }

    [Fact]
    public async Task PushBackendPayload_EmptyList_NoKafkaMessages()
    {
        await _mutation.PushBackendPayload(_project.Id.ToString(), [], _kafka, CancellationToken.None);
        Assert.Equal(0, _kafka.BackendErrorCount);
    }

    // ── PushMetrics ────────────────────────────────────────────────────

    [Fact]
    public async Task PushMetrics_ProducesForEachMetric()
    {
        var metrics = new List<MetricInput>
        {
            new("kafka-session-1", null, null, null, null, "cpu", 85.5, null, DateTime.UtcNow, null),
            new("kafka-session-1", null, null, null, null, "memory", 72.0, null, DateTime.UtcNow, null),
            new("kafka-session-1", null, null, null, null, "disk", 45.0, null, DateTime.UtcNow, null),
        };

        await _mutation.PushMetrics(metrics, _kafka, CancellationToken.None);
        Assert.Equal(3, _kafka.MetricCount);
    }

    [Fact]
    public async Task PushMetrics_EmptyList_NoKafkaMessages()
    {
        await _mutation.PushMetrics([], _kafka, CancellationToken.None);
        Assert.Equal(0, _kafka.MetricCount);
    }

    // ── KafkaProducerAdapter Tests ─────────────────────────────────────

    [Fact]
    public void KafkaProducerAdapter_LogInput_Serializable()
    {
        var log = new LogInput(
            ProjectId: 1,
            Timestamp: DateTime.UtcNow,
            TraceId: "trace-1",
            SpanId: "span-1",
            SecureSessionId: "sess-1",
            SeverityText: "ERROR",
            SeverityNumber: 17,
            Source: "otel",
            ServiceName: "svc",
            ServiceVersion: "1.0",
            Body: "test log",
            LogAttributes: new Dictionary<string, string> { ["key"] = "val" },
            Environment: "prod");

        var json = JsonSerializer.Serialize(log);
        var deserialized = JsonSerializer.Deserialize<LogInput>(json);
        Assert.NotNull(deserialized);
        Assert.Equal("ERROR", deserialized!.SeverityText);
    }

    [Fact]
    public void KafkaProducerAdapter_TraceInput_Serializable()
    {
        var trace = new TraceInput(
            ProjectId: 1,
            Timestamp: DateTime.UtcNow,
            TraceId: "trace-1",
            SpanId: "span-1",
            ParentSpanId: "parent-1",
            SecureSessionId: "sess-1",
            ServiceName: "svc",
            ServiceVersion: "1.0",
            Environment: "prod",
            SpanName: "GET /api",
            SpanKind: "SERVER",
            Duration: 100_000,
            StatusCode: "OK",
            StatusMessage: "",
            TraceAttributes: new Dictionary<string, string> { ["http.method"] = "GET" },
            HasErrors: false);

        var json = JsonSerializer.Serialize(trace);
        var deserialized = JsonSerializer.Deserialize<TraceInput>(json);
        Assert.NotNull(deserialized);
        Assert.Equal("GET /api", deserialized!.SpanName);
        Assert.False(deserialized.HasErrors);
    }

    [Fact]
    public async Task ProduceLog_TracksMessage()
    {
        var log = new LogInput(1, DateTime.UtcNow, "t", "s", "", "INFO", 9, "", "svc", "1.0", "body", null, "");
        await _kafka.ProduceLogAsync(log, CancellationToken.None);
        Assert.Equal(1, _kafka.LogCount);
    }

    [Fact]
    public async Task ProduceTrace_TracksMessage()
    {
        var trace = new TraceInput(1, DateTime.UtcNow, "t", "s", "", "", "svc", "1.0", "", "span", "SERVER", 0, "OK", "", null, false);
        await _kafka.ProduceTraceAsync(trace, CancellationToken.None);
        Assert.Equal(1, _kafka.TraceCount);
    }

    // ── Stub Kafka Producer ────────────────────────────────────────────

    private class StubKafkaProducer : IKafkaProducer
    {
        public int SessionEventsCount { get; private set; }
        public int BackendErrorCount { get; private set; }
        public int MetricCount { get; private set; }
        public int LogCount { get; private set; }
        public int TraceCount { get; private set; }
        public string? LastSessionSecureId { get; private set; }
        public string? LastSessionData { get; private set; }
        public long LastPayloadId { get; private set; }

        public Task ProduceSessionEventsAsync(string sessionSecureId, long payloadId, string data, CancellationToken ct)
        {
            SessionEventsCount++;
            LastSessionSecureId = sessionSecureId;
            LastPayloadId = payloadId;
            LastSessionData = data;
            return Task.CompletedTask;
        }

        public Task ProduceBackendErrorAsync(string? projectId, BackendErrorObjectInput error, CancellationToken ct)
        {
            BackendErrorCount++;
            return Task.CompletedTask;
        }

        public Task ProduceMetricAsync(MetricInput metric, CancellationToken ct)
        {
            MetricCount++;
            return Task.CompletedTask;
        }

        public Task ProduceLogAsync(LogInput log, CancellationToken ct)
        {
            LogCount++;
            return Task.CompletedTask;
        }

        public Task ProduceTraceAsync(TraceInput trace, CancellationToken ct)
        {
            TraceCount++;
            return Task.CompletedTask;
        }
    }
}
