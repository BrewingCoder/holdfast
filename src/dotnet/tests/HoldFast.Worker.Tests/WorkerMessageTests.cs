using System.Text.Json;
using HoldFast.Data.ClickHouse.Models;
using HoldFast.Worker;
using Xunit;

namespace HoldFast.Worker.Tests;

/// <summary>
/// Tests for Kafka worker message serialization, deserialization, and edge cases.
/// Covers LogIngestionMessage, TraceIngestionMessage, and existing message types.
/// </summary>
public class WorkerMessageTests
{
    // ── LogIngestionMessage ────────────────────────────────────────────

    [Fact]
    public void LogIngestionMessage_Roundtrip()
    {
        var msg = new LogIngestionMessage(
            ProjectId: 42,
            Timestamp: new DateTime(2025, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            TraceId: "trace-abc",
            SpanId: "span-def",
            SecureSessionId: "session-123",
            SeverityText: "ERROR",
            SeverityNumber: 17,
            Source: "otel",
            ServiceName: "payment-svc",
            ServiceVersion: "2.0.0",
            Body: "Failed to process payment",
            LogAttributes: new Dictionary<string, string> { ["user_id"] = "u123" },
            Environment: "production");

        var json = JsonSerializer.Serialize(msg);
        var deserialized = JsonSerializer.Deserialize<LogIngestionMessage>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(42, deserialized!.ProjectId);
        Assert.Equal("ERROR", deserialized.SeverityText);
        Assert.Equal(17, deserialized.SeverityNumber);
        Assert.Equal("payment-svc", deserialized.ServiceName);
        Assert.Equal("Failed to process payment", deserialized.Body);
        Assert.Equal("u123", deserialized.LogAttributes!["user_id"]);
    }

    [Fact]
    public void LogIngestionMessage_NullAttributes()
    {
        var msg = new LogIngestionMessage(
            1, DateTime.UtcNow, "", "", "", "INFO", 9, "", "svc", "1.0", "body", null, "dev");

        var json = JsonSerializer.Serialize(msg);
        var deserialized = JsonSerializer.Deserialize<LogIngestionMessage>(json);
        Assert.Null(deserialized!.LogAttributes);
    }

    [Fact]
    public void LogIngestionMessage_EmptyBody()
    {
        var msg = new LogIngestionMessage(
            1, DateTime.UtcNow, "", "", "", "DEBUG", 5, "", "", "", "", null, "");
        Assert.Equal("", msg.Body);
    }

    [Fact]
    public void LogIngestionMessage_AllSeverityLevels()
    {
        var levels = new[] { "TRACE", "DEBUG", "INFO", "WARN", "ERROR", "FATAL" };
        foreach (var level in levels)
        {
            var msg = new LogIngestionMessage(1, DateTime.UtcNow, "", "", "", level, 0, "", "", "", "test", null, "");
            Assert.Equal(level, msg.SeverityText);
        }
    }

    // ── TraceIngestionMessage ──────────────────────────────────────────

    [Fact]
    public void TraceIngestionMessage_Roundtrip()
    {
        var msg = new TraceIngestionMessage(
            ProjectId: 7,
            Timestamp: new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            TraceId: "0af7651916cd43dd8448eb211c80319c",
            SpanId: "b7ad6b7169203331",
            ParentSpanId: "00f067aa0ba902b7",
            SecureSessionId: "sess-xyz",
            ServiceName: "api-gateway",
            ServiceVersion: "3.2.1",
            Environment: "staging",
            SpanName: "GET /api/users",
            SpanKind: "SERVER",
            Duration: 125_000_000, // 125ms in nanoseconds
            StatusCode: "OK",
            StatusMessage: "",
            TraceAttributes: new Dictionary<string, string>
            {
                ["http.method"] = "GET",
                ["http.status_code"] = "200",
                ["http.url"] = "https://api.example.com/users",
            },
            HasErrors: false);

        var json = JsonSerializer.Serialize(msg);
        var deserialized = JsonSerializer.Deserialize<TraceIngestionMessage>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("api-gateway", deserialized!.ServiceName);
        Assert.Equal("GET /api/users", deserialized.SpanName);
        Assert.Equal(125_000_000, deserialized.Duration);
        Assert.False(deserialized.HasErrors);
        Assert.Equal("200", deserialized.TraceAttributes!["http.status_code"]);
    }

    [Fact]
    public void TraceIngestionMessage_WithErrors()
    {
        var msg = new TraceIngestionMessage(
            1, DateTime.UtcNow, "t1", "s1", "", "", "svc", "1.0", "prod",
            "db.query", "CLIENT", 5000, "ERROR", "connection timeout",
            new Dictionary<string, string> { ["db.system"] = "postgresql" },
            HasErrors: true);

        Assert.True(msg.HasErrors);
        Assert.Equal("ERROR", msg.StatusCode);
    }

    [Fact]
    public void TraceIngestionMessage_NullAttributes()
    {
        var msg = new TraceIngestionMessage(
            1, DateTime.UtcNow, "", "", "", "", "", "", "", "", "", 0, "", "", null, false);
        Assert.Null(msg.TraceAttributes);
    }

    [Fact]
    public void TraceIngestionMessage_ZeroDuration()
    {
        var msg = new TraceIngestionMessage(
            1, DateTime.UtcNow, "", "", "", "", "", "", "", "instant", "INTERNAL", 0, "OK", "", null, false);
        Assert.Equal(0, msg.Duration);
    }

    [Fact]
    public void TraceIngestionMessage_VeryLongDuration()
    {
        var msg = new TraceIngestionMessage(
            1, DateTime.UtcNow, "", "", "", "", "", "", "", "long", "SERVER",
            3_600_000_000_000L, // 1 hour in nanoseconds
            "OK", "", null, false);
        Assert.Equal(3_600_000_000_000L, msg.Duration);
    }

    // ── WriteInput Models ──────────────────────────────────────────────

    [Fact]
    public void LogRowInput_DefaultValues()
    {
        var input = new LogRowInput();
        Assert.Equal(0, input.ProjectId);
        Assert.Equal(string.Empty, input.TraceId);
        Assert.Equal(string.Empty, input.Body);
        Assert.NotNull(input.LogAttributes);
        Assert.Empty(input.LogAttributes);
    }

    [Fact]
    public void TraceRowInput_DefaultValues()
    {
        var input = new TraceRowInput();
        Assert.Equal(0, input.ProjectId);
        Assert.Equal(string.Empty, input.SpanName);
        Assert.Equal(0, input.Duration);
        Assert.False(input.HasErrors);
        Assert.NotNull(input.TraceAttributes);
    }

    [Fact]
    public void LogRowInput_WithAttributes()
    {
        var input = new LogRowInput
        {
            ProjectId = 5,
            Body = "User logged in",
            LogAttributes = new Dictionary<string, string>
            {
                ["user.id"] = "12345",
                ["session.type"] = "web",
            },
        };

        Assert.Equal(2, input.LogAttributes.Count);
        Assert.Equal("12345", input.LogAttributes["user.id"]);
    }

    [Fact]
    public void TraceRowInput_SpanKindValues()
    {
        var kinds = new[] { "SERVER", "CLIENT", "PRODUCER", "CONSUMER", "INTERNAL" };
        foreach (var kind in kinds)
        {
            var input = new TraceRowInput { SpanKind = kind };
            Assert.Equal(kind, input.SpanKind);
        }
    }

    // ── BackendErrorMessage Extensions ─────────────────────────────────

    [Fact]
    public void BackendErrorMessage_WithAllNullOptionals()
    {
        var msg = new BackendErrorMessage(
            null, "error event", "BACKEND", "url", "source", "stack",
            DateTime.UtcNow, null, "svc", "1.0", "prod", null, null, null);

        Assert.Null(msg.ProjectId);
        Assert.Null(msg.Payload);
        Assert.Null(msg.SessionSecureId);
        Assert.Null(msg.TraceId);
        Assert.Null(msg.SpanId);
    }

    [Fact]
    public void SessionEventsMessage_Roundtrip()
    {
        var msg = new SessionEventsMessage("secure-abc", 42L, "base64data==");
        var json = JsonSerializer.Serialize(msg);
        var deserialized = JsonSerializer.Deserialize<SessionEventsMessage>(json);

        Assert.Equal("secure-abc", deserialized!.SessionSecureId);
        Assert.Equal(42L, deserialized.PayloadId);
        Assert.Equal("base64data==", deserialized.Data);
    }

    [Fact]
    public void MetricsMessage_WithTags()
    {
        var msg = new MetricsMessage(
            "session-1", "cpu.usage", 85.5, "system",
            DateTime.UtcNow,
            new Dictionary<string, string> { ["host"] = "web-01", ["region"] = "us-east" });

        Assert.Equal(2, msg.Tags!.Count);
        Assert.Equal("web-01", msg.Tags["host"]);
    }
}
