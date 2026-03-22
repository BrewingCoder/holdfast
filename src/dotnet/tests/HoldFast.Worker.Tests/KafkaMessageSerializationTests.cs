using System.Text.Json;
using HoldFast.Worker;
using Xunit;

namespace HoldFast.Worker.Tests;

/// <summary>
/// Tests JSON serialization/deserialization of Kafka message types.
/// These types must round-trip through System.Text.Json to work with KafkaConsumerService.
/// </summary>
public class KafkaMessageSerializationTests
{
    // ══════════════════════════════════════════════════════════════════
    // MetricsMessage
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void MetricsMessage_RoundTrip()
    {
        var ts = new DateTime(2026, 3, 20, 10, 0, 0, DateTimeKind.Utc);
        var msg = new MetricsMessage("sess-1", "LCP", 2.5, "web-vital", ts,
            new Dictionary<string, string> { ["page"] = "/home" });

        var json = JsonSerializer.Serialize(msg);
        var deserialized = JsonSerializer.Deserialize<MetricsMessage>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("sess-1", deserialized!.SessionSecureId);
        Assert.Equal("LCP", deserialized.Name);
        Assert.Equal(2.5, deserialized.Value);
        Assert.Equal("web-vital", deserialized.Category);
        Assert.Equal(ts, deserialized.Timestamp);
        Assert.Equal("/home", deserialized.Tags!["page"]);
    }

    [Fact]
    public void MetricsMessage_NullOptionalFields_RoundTrip()
    {
        var msg = new MetricsMessage("s", "m", 0.0, null, DateTime.UtcNow, null);
        var json = JsonSerializer.Serialize(msg);
        var deserialized = JsonSerializer.Deserialize<MetricsMessage>(json);

        Assert.Null(deserialized!.Category);
        Assert.Null(deserialized.Tags);
    }

    [Fact]
    public void MetricsMessage_EmptyTags_RoundTrip()
    {
        var msg = new MetricsMessage("s", "m", 1.0, null, DateTime.UtcNow, new());
        var json = JsonSerializer.Serialize(msg);
        var deserialized = JsonSerializer.Deserialize<MetricsMessage>(json);

        Assert.NotNull(deserialized!.Tags);
        Assert.Empty(deserialized.Tags);
    }

    [Fact]
    public void MetricsMessage_SpecialCharInName()
    {
        var msg = new MetricsMessage("s", "metric.name/with-special_chars", 1.0, null, DateTime.UtcNow, null);
        var json = JsonSerializer.Serialize(msg);
        var deserialized = JsonSerializer.Deserialize<MetricsMessage>(json);

        Assert.Equal("metric.name/with-special_chars", deserialized!.Name);
    }

    [Fact]
    public void MetricsMessage_VeryLargeValue()
    {
        var msg = new MetricsMessage("s", "m", double.MaxValue, null, DateTime.UtcNow, null);
        var json = JsonSerializer.Serialize(msg);
        var deserialized = JsonSerializer.Deserialize<MetricsMessage>(json);

        Assert.Equal(double.MaxValue, deserialized!.Value);
    }

    // ══════════════════════════════════════════════════════════════════
    // LogIngestionMessage
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void LogIngestionMessage_RoundTrip()
    {
        var ts = new DateTime(2026, 3, 20, 15, 0, 0, DateTimeKind.Utc);
        var attrs = new Dictionary<string, string>
        {
            ["http.method"] = "POST",
            ["http.url"] = "/api/data",
        };
        var msg = new LogIngestionMessage(
            42, ts, "trace-1", "span-1", "sess-1",
            "ERROR", 17, "otel", "api", "2.0", "Error msg", attrs, "prod");

        var json = JsonSerializer.Serialize(msg);
        var d = JsonSerializer.Deserialize<LogIngestionMessage>(json)!;

        Assert.Equal(42, d.ProjectId);
        Assert.Equal(ts, d.Timestamp);
        Assert.Equal("trace-1", d.TraceId);
        Assert.Equal("span-1", d.SpanId);
        Assert.Equal("sess-1", d.SecureSessionId);
        Assert.Equal("ERROR", d.SeverityText);
        Assert.Equal(17, d.SeverityNumber);
        Assert.Equal("otel", d.Source);
        Assert.Equal("api", d.ServiceName);
        Assert.Equal("2.0", d.ServiceVersion);
        Assert.Equal("Error msg", d.Body);
        Assert.Equal("prod", d.Environment);
        Assert.Equal(2, d.LogAttributes!.Count);
    }

    [Fact]
    public void LogIngestionMessage_NullAttributes_RoundTrip()
    {
        var msg = new LogIngestionMessage(1, DateTime.UtcNow, "t", "s", "sess",
            "INFO", 9, "src", "svc", "1.0", "body", null, "dev");
        var json = JsonSerializer.Serialize(msg);
        var d = JsonSerializer.Deserialize<LogIngestionMessage>(json)!;

        Assert.Null(d.LogAttributes);
    }

    [Fact]
    public void LogIngestionMessage_UnicodeBody_RoundTrip()
    {
        var body = "エラーが発生しました: NullPointerException 🔥";
        var msg = new LogIngestionMessage(1, DateTime.UtcNow, "t", "s", "sess",
            "ERROR", 17, "src", "svc", "1.0", body, null, "dev");
        var json = JsonSerializer.Serialize(msg);
        var d = JsonSerializer.Deserialize<LogIngestionMessage>(json)!;

        Assert.Equal(body, d.Body);
    }

    [Fact]
    public void LogIngestionMessage_VeryLongBody_RoundTrip()
    {
        var body = new string('x', 100_000);
        var msg = new LogIngestionMessage(1, DateTime.UtcNow, "t", "s", "sess",
            "DEBUG", 5, "src", "svc", "1.0", body, null, "dev");
        var json = JsonSerializer.Serialize(msg);
        var d = JsonSerializer.Deserialize<LogIngestionMessage>(json)!;

        Assert.Equal(100_000, d.Body.Length);
    }

    // ══════════════════════════════════════════════════════════════════
    // TraceIngestionMessage
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void TraceIngestionMessage_RoundTrip()
    {
        var ts = new DateTime(2026, 3, 20, 16, 0, 0, DateTimeKind.Utc);
        var attrs = new Dictionary<string, string>
        {
            ["http.status_code"] = "200",
            ["db.system"] = "postgresql",
        };
        var msg = new TraceIngestionMessage(
            42, ts, "trace-abc", "span-001", "parent-000",
            "sess-1", "api", "3.0", "staging",
            "GET /health", "SERVER", 1500, "OK", "",
            attrs, false);

        var json = JsonSerializer.Serialize(msg);
        var d = JsonSerializer.Deserialize<TraceIngestionMessage>(json)!;

        Assert.Equal(42, d.ProjectId);
        Assert.Equal(ts, d.Timestamp);
        Assert.Equal("trace-abc", d.TraceId);
        Assert.Equal("span-001", d.SpanId);
        Assert.Equal("parent-000", d.ParentSpanId);
        Assert.Equal("sess-1", d.SecureSessionId);
        Assert.Equal("api", d.ServiceName);
        Assert.Equal("GET /health", d.SpanName);
        Assert.Equal("SERVER", d.SpanKind);
        Assert.Equal(1500, d.Duration);
        Assert.Equal("OK", d.StatusCode);
        Assert.False(d.HasErrors);
        Assert.Equal(2, d.TraceAttributes!.Count);
    }

    [Fact]
    public void TraceIngestionMessage_NullAttributes_RoundTrip()
    {
        var msg = new TraceIngestionMessage(1, DateTime.UtcNow, "t", "s", "",
            "", "svc", "1.0", "dev", "span", "CLIENT", 100, "OK", "",
            null, false);
        var json = JsonSerializer.Serialize(msg);
        var d = JsonSerializer.Deserialize<TraceIngestionMessage>(json)!;

        Assert.Null(d.TraceAttributes);
    }

    [Fact]
    public void TraceIngestionMessage_HasErrors_True_RoundTrip()
    {
        var msg = new TraceIngestionMessage(1, DateTime.UtcNow, "t", "s", "",
            "", "svc", "1.0", "prod", "span", "SERVER", 5000,
            "ERROR", "Internal", null, true);
        var json = JsonSerializer.Serialize(msg);
        var d = JsonSerializer.Deserialize<TraceIngestionMessage>(json)!;

        Assert.True(d.HasErrors);
    }

    [Fact]
    public void TraceIngestionMessage_MaxDuration_RoundTrip()
    {
        var msg = new TraceIngestionMessage(1, DateTime.UtcNow, "t", "s", "",
            "", "svc", "1.0", "prod", "slow", "SERVER", long.MaxValue,
            "OK", "", null, false);
        var json = JsonSerializer.Serialize(msg);
        var d = JsonSerializer.Deserialize<TraceIngestionMessage>(json)!;

        Assert.Equal(long.MaxValue, d.Duration);
    }

    // ══════════════════════════════════════════════════════════════════
    // Cross-message: JSON produced by one type shouldn't deserialize to another
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void MetricsMessage_JsonDoesNotMatchLogMessage()
    {
        var msg = new MetricsMessage("s", "m", 1.0, null, DateTime.UtcNow, null);
        var json = JsonSerializer.Serialize(msg);

        // Deserializing as LogIngestionMessage should produce an object with wrong/default values
        var asLog = JsonSerializer.Deserialize<LogIngestionMessage>(json);
        Assert.NotNull(asLog);
        Assert.Equal(0, asLog!.ProjectId); // Not a valid log
    }
}
