using System.Text.Json;
using HoldFast.GraphQL.Public;
using HoldFast.GraphQL.Public.InputTypes;
using Xunit;

namespace HoldFast.GraphQL.Tests;

/// <summary>
/// Tests for GraphQL input type records: serialization, default values, edge cases.
/// </summary>
public class InputTypeExtendedTests
{
    // ── ErrorObjectInput ──────────────────────────────────────────────

    [Fact]
    public void ErrorObjectInput_Roundtrip()
    {
        var input = new ErrorObjectInput(
            Event: "TypeError: undefined is not a function",
            Type: "FRONTEND",
            Url: "https://app.example.com/dashboard",
            Source: "window.onerror",
            LineNumber: 42,
            ColumnNumber: 13,
            StackTrace:
            [
                new StackFrameInput("onClick", "app.js", 42, 13, false, false, ""),
                new StackFrameInput("handleEvent", "react-dom.js", 100, 1, false, false, null),
            ],
            Timestamp: new DateTime(2025, 3, 15, 10, 30, 0, DateTimeKind.Utc),
            Payload: "{\"extra\":\"data\"}");

        var json = JsonSerializer.Serialize(input);
        var deserialized = JsonSerializer.Deserialize<ErrorObjectInput>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("TypeError: undefined is not a function", deserialized!.Event);
        Assert.Equal("FRONTEND", deserialized.Type);
        Assert.Equal(42, deserialized.LineNumber);
        Assert.Equal(2, deserialized.StackTrace.Count);
        Assert.Equal("onClick", deserialized.StackTrace[0].FunctionName);
    }

    [Fact]
    public void ErrorObjectInput_NullPayload()
    {
        var input = new ErrorObjectInput("err", "BACKEND", "url", "src", 1, 1, [], DateTime.UtcNow, null);
        Assert.Null(input.Payload);
    }

    [Fact]
    public void ErrorObjectInput_EmptyStackTrace()
    {
        var input = new ErrorObjectInput("err", "BACKEND", "url", "src", 0, 0, [], DateTime.UtcNow, null);
        Assert.Empty(input.StackTrace);
    }

    // ── StackFrameInput ───────────────────────────────────────────────

    [Fact]
    public void StackFrameInput_AllNulls()
    {
        var frame = new StackFrameInput(null, null, null, null, null, null, null);
        Assert.Null(frame.FunctionName);
        Assert.Null(frame.FileName);
        Assert.Null(frame.LineNumber);
        Assert.Null(frame.ColumnNumber);
        Assert.Null(frame.IsEval);
        Assert.Null(frame.IsNative);
        Assert.Null(frame.Source);
    }

    [Fact]
    public void StackFrameInput_Roundtrip()
    {
        var frame = new StackFrameInput("main", "app.go", 100, 5, true, false, "compiled");
        var json = JsonSerializer.Serialize(frame);
        var back = JsonSerializer.Deserialize<StackFrameInput>(json);

        Assert.Equal("main", back!.FunctionName);
        Assert.Equal(100, back.LineNumber);
        Assert.True(back.IsEval);
        Assert.False(back.IsNative);
    }

    [Fact]
    public void StackFrameInput_EvalAndNativeFlags()
    {
        var evalFrame = new StackFrameInput("eval", "eval", 1, 1, true, false, null);
        var nativeFrame = new StackFrameInput("[native code]", null, null, null, false, true, null);

        Assert.True(evalFrame.IsEval);
        Assert.False(evalFrame.IsNative);
        Assert.False(nativeFrame.IsEval);
        Assert.True(nativeFrame.IsNative);
    }

    // ── BackendErrorObjectInput ───────────────────────────────────────

    [Fact]
    public void BackendErrorObjectInput_Roundtrip()
    {
        var input = new BackendErrorObjectInput(
            SessionSecureId: "sess-abc",
            RequestId: "req-123",
            TraceId: "trace-456",
            SpanId: "span-789",
            LogCursor: "cursor-001",
            Event: "panic: runtime error",
            Type: "BACKEND",
            Url: "/api/v1/users",
            Source: "main.go",
            StackTrace: "goroutine 1:\nmain.go:42",
            Timestamp: DateTime.UtcNow,
            Payload: null,
            Service: new ServiceInput("user-service", "1.2.3"),
            Environment: "production");

        var json = JsonSerializer.Serialize(input);
        var back = JsonSerializer.Deserialize<BackendErrorObjectInput>(json);

        Assert.Equal("panic: runtime error", back!.Event);
        Assert.Equal("user-service", back.Service.Name);
        Assert.Equal("1.2.3", back.Service.Version);
        Assert.Equal("production", back.Environment);
    }

    [Fact]
    public void BackendErrorObjectInput_NullOptionals()
    {
        var input = new BackendErrorObjectInput(
            null, null, null, null, null,
            "error", "BACKEND", "", "", "", DateTime.UtcNow, null,
            new ServiceInput("svc", "1.0"), "dev");

        Assert.Null(input.SessionSecureId);
        Assert.Null(input.RequestId);
        Assert.Null(input.TraceId);
        Assert.Null(input.SpanId);
        Assert.Null(input.LogCursor);
        Assert.Null(input.Payload);
    }

    // ── ServiceInput ──────────────────────────────────────────────────

    [Fact]
    public void ServiceInput_Roundtrip()
    {
        var svc = new ServiceInput("payment-api", "3.0.0-beta.1");
        var json = JsonSerializer.Serialize(svc);
        var back = JsonSerializer.Deserialize<ServiceInput>(json);
        Assert.Equal("payment-api", back!.Name);
        Assert.Equal("3.0.0-beta.1", back.Version);
    }

    [Fact]
    public void ServiceInput_EmptyStrings()
    {
        var svc = new ServiceInput("", "");
        Assert.Equal("", svc.Name);
        Assert.Equal("", svc.Version);
    }

    // ── LogInput ──────────────────────────────────────────────────────

    [Fact]
    public void LogInput_Roundtrip()
    {
        var input = new LogInput(
            ProjectId: 10,
            Timestamp: new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            TraceId: "t1",
            SpanId: "s1",
            SecureSessionId: "sess",
            SeverityText: "ERROR",
            SeverityNumber: 17,
            Source: "otel",
            ServiceName: "api",
            ServiceVersion: "2.0",
            Body: "Connection refused",
            LogAttributes: new Dictionary<string, string> { ["db"] = "postgres" },
            Environment: "staging");

        var json = JsonSerializer.Serialize(input);
        var back = JsonSerializer.Deserialize<LogInput>(json);

        Assert.Equal(10, back!.ProjectId);
        Assert.Equal("ERROR", back.SeverityText);
        Assert.Equal(17, back.SeverityNumber);
        Assert.Equal("postgres", back.LogAttributes!["db"]);
    }

    [Fact]
    public void LogInput_NullAttributes()
    {
        var input = new LogInput(1, DateTime.UtcNow, "", "", "", "INFO", 9, "", "", "", "", null, "");
        Assert.Null(input.LogAttributes);
    }

    [Fact]
    public void LogInput_AllSeverityLevels()
    {
        var levels = new[] { ("TRACE", 1), ("DEBUG", 5), ("INFO", 9), ("WARN", 13), ("ERROR", 17), ("FATAL", 21) };
        foreach (var (text, number) in levels)
        {
            var input = new LogInput(1, DateTime.UtcNow, "", "", "", text, number, "", "", "", "", null, "");
            Assert.Equal(text, input.SeverityText);
            Assert.Equal(number, input.SeverityNumber);
        }
    }

    // ── TraceInput ────────────────────────────────────────────────────

    [Fact]
    public void TraceInput_Roundtrip()
    {
        var input = new TraceInput(
            ProjectId: 5,
            Timestamp: DateTime.UtcNow,
            TraceId: "abc123",
            SpanId: "span1",
            ParentSpanId: "parent1",
            SecureSessionId: "sess",
            ServiceName: "gateway",
            ServiceVersion: "1.0",
            Environment: "prod",
            SpanName: "GET /api/health",
            SpanKind: "SERVER",
            Duration: 50_000_000,
            StatusCode: "OK",
            StatusMessage: "",
            TraceAttributes: new Dictionary<string, string> { ["http.method"] = "GET" },
            HasErrors: false);

        var json = JsonSerializer.Serialize(input);
        var back = JsonSerializer.Deserialize<TraceInput>(json);

        Assert.Equal("gateway", back!.ServiceName);
        Assert.Equal("GET /api/health", back.SpanName);
        Assert.Equal(50_000_000, back.Duration);
        Assert.False(back.HasErrors);
    }

    [Fact]
    public void TraceInput_WithErrors()
    {
        var input = new TraceInput(1, DateTime.UtcNow, "", "", "", "", "", "", "",
            "db.query", "CLIENT", 0, "ERROR", "timeout", null, true);
        Assert.True(input.HasErrors);
        Assert.Equal("ERROR", input.StatusCode);
    }

    [Fact]
    public void TraceInput_SpanKinds()
    {
        var kinds = new[] { "UNSPECIFIED", "INTERNAL", "SERVER", "CLIENT", "PRODUCER", "CONSUMER" };
        foreach (var kind in kinds)
        {
            var input = new TraceInput(1, DateTime.UtcNow, "", "", "", "", "", "", "",
                "", kind, 0, "", "", null, false);
            Assert.Equal(kind, input.SpanKind);
        }
    }

    // ── MetricInput ───────────────────────────────────────────────────

    [Fact]
    public void MetricInput_Roundtrip()
    {
        var input = new MetricInput(
            SessionSecureId: "sess-1",
            SpanId: "span-1",
            ParentSpanId: "parent-1",
            TraceId: "trace-1",
            Group: "performance",
            Name: "page_load_time",
            Value: 1234.5,
            Category: "web-vital",
            Timestamp: DateTime.UtcNow,
            Tags: [new MetricTag("page", "/home"), new MetricTag("browser", "chrome")]);

        var json = JsonSerializer.Serialize(input);
        var back = JsonSerializer.Deserialize<MetricInput>(json);

        Assert.Equal("page_load_time", back!.Name);
        Assert.Equal(1234.5, back.Value);
        Assert.Equal(2, back.Tags!.Count);
    }

    [Fact]
    public void MetricInput_NullOptionals()
    {
        var input = new MetricInput("sess", null, null, null, null, "cpu", 50.0, null, DateTime.UtcNow, null);
        Assert.Null(input.SpanId);
        Assert.Null(input.ParentSpanId);
        Assert.Null(input.TraceId);
        Assert.Null(input.Group);
        Assert.Null(input.Category);
        Assert.Null(input.Tags);
    }

    [Fact]
    public void MetricInput_ZeroValue()
    {
        var input = new MetricInput("sess", null, null, null, null, "counter", 0.0, null, DateTime.UtcNow, null);
        Assert.Equal(0.0, input.Value);
    }

    [Fact]
    public void MetricInput_NegativeValue()
    {
        var input = new MetricInput("sess", null, null, null, null, "delta", -42.5, null, DateTime.UtcNow, null);
        Assert.Equal(-42.5, input.Value);
    }

    [Fact]
    public void MetricInput_VeryLargeValue()
    {
        var input = new MetricInput("sess", null, null, null, null, "big", double.MaxValue, null, DateTime.UtcNow, null);
        Assert.Equal(double.MaxValue, input.Value);
    }

    // ── MetricTag ─────────────────────────────────────────────────────

    [Fact]
    public void MetricTag_Roundtrip()
    {
        var tag = new MetricTag("region", "us-east-1");
        var json = JsonSerializer.Serialize(tag);
        var back = JsonSerializer.Deserialize<MetricTag>(json);
        Assert.Equal("region", back!.Name);
        Assert.Equal("us-east-1", back.Value);
    }

    [Fact]
    public void MetricTag_EmptyStrings()
    {
        var tag = new MetricTag("", "");
        Assert.Equal("", tag.Name);
        Assert.Equal("", tag.Value);
    }

    // ── SamplingConfig ────────────────────────────────────────────────

    [Fact]
    public void SamplingConfig_Defaults()
    {
        var config = new SamplingConfig();
        Assert.Null(config.Spans);
        Assert.Null(config.Logs);
    }

    [Fact]
    public void SamplingConfig_WithSpans()
    {
        var config = new SamplingConfig(
            Spans:
            [
                new SpanSamplingConfig(
                    Name: new MatchConfig(RegexValue: "GET.*"),
                    SamplingRatio: 10),
            ]);

        Assert.NotNull(config.Spans);
        Assert.Single(config.Spans);
        Assert.Equal(10, config.Spans[0].SamplingRatio);
        Assert.Equal("GET.*", config.Spans[0].Name!.RegexValue);
    }

    [Fact]
    public void SamplingConfig_WithLogs()
    {
        var config = new SamplingConfig(
            Logs:
            [
                new LogSamplingConfig(
                    SeverityText: new MatchConfig(MatchValue: "DEBUG"),
                    SamplingRatio: 100),
            ]);

        Assert.Single(config.Logs!);
        Assert.Equal(100, config.Logs[0].SamplingRatio);
    }

    [Fact]
    public void SpanSamplingConfig_WithAttributes()
    {
        var config = new SpanSamplingConfig(
            Attributes:
            [
                new AttributeMatchConfig(
                    Key: new MatchConfig(MatchValue: "http.method"),
                    Attribute: new MatchConfig(MatchValue: "GET")),
            ],
            SamplingRatio: 5);

        Assert.Single(config.Attributes!);
        Assert.Equal(5, config.SamplingRatio);
    }

    [Fact]
    public void SpanSamplingConfig_WithEvents()
    {
        var config = new SpanSamplingConfig(
            Events:
            [
                new SpanEventMatchConfig(
                    Name: new MatchConfig(RegexValue: "exception.*"),
                    Attributes:
                    [
                        new AttributeMatchConfig(
                            Key: new MatchConfig(MatchValue: "exception.type"),
                            Attribute: new MatchConfig(RegexValue: ".*Timeout.*")),
                    ]),
            ]);

        Assert.Single(config.Events!);
        Assert.Equal("exception.*", config.Events[0].Name!.RegexValue);
    }

    [Fact]
    public void SpanSamplingConfig_DefaultSamplingRatio()
    {
        var config = new SpanSamplingConfig();
        Assert.Equal(1, config.SamplingRatio);
    }

    [Fact]
    public void LogSamplingConfig_DefaultSamplingRatio()
    {
        var config = new LogSamplingConfig();
        Assert.Equal(1, config.SamplingRatio);
    }

    [Fact]
    public void MatchConfig_Defaults()
    {
        var config = new MatchConfig();
        Assert.Null(config.RegexValue);
        Assert.Null(config.MatchValue);
    }

    [Fact]
    public void SamplingConfig_Roundtrip()
    {
        var config = new SamplingConfig(
            Spans: [new SpanSamplingConfig(Name: new MatchConfig(RegexValue: ".*"), SamplingRatio: 50)],
            Logs: [new LogSamplingConfig(Message: new MatchConfig(MatchValue: "health"), SamplingRatio: 1000)]);

        var json = JsonSerializer.Serialize(config);
        var back = JsonSerializer.Deserialize<SamplingConfig>(json);

        Assert.Single(back!.Spans!);
        Assert.Equal(50, back.Spans[0].SamplingRatio);
        Assert.Single(back.Logs!);
        Assert.Equal(1000, back.Logs[0].SamplingRatio);
    }

    // ── SearchResults ─────────────────────────────────────────────────

    [Fact]
    public void ErrorGroupSearchResult_Defaults()
    {
        var result = new HoldFast.GraphQL.Private.ErrorGroupSearchResult();
        Assert.Empty(result.ErrorGroupIds);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public void ErrorGroupSearchResult_WithData()
    {
        var result = new HoldFast.GraphQL.Private.ErrorGroupSearchResult
        {
            ErrorGroupIds = [1, 2, 3, 42],
            TotalCount = 100,
        };
        Assert.Equal(4, result.ErrorGroupIds.Count);
        Assert.Equal(100, result.TotalCount);
    }

    [Fact]
    public void SessionSearchResult_Defaults()
    {
        var result = new HoldFast.GraphQL.Private.SessionSearchResult();
        Assert.Empty(result.SessionIds);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public void SessionSearchResult_WithData()
    {
        var result = new HoldFast.GraphQL.Private.SessionSearchResult
        {
            SessionIds = [10, 20, 30],
            TotalCount = 500,
        };
        Assert.Equal(3, result.SessionIds.Count);
        Assert.Equal(500, result.TotalCount);
    }

    // ── PublicQuery ───────────────────────────────────────────────────

    [Fact]
    public void PublicQuery_Ignore_ReturnsNull()
    {
        var query = new PublicQuery();
        Assert.Null(query.Ignore(0));
        Assert.Null(query.Ignore(1));
        Assert.Null(query.Ignore(int.MaxValue));
        Assert.Null(query.Ignore(-1));
    }
}
