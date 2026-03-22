namespace HoldFast.Worker.Tests;

public class SessionEventsMessageTests
{
    [Fact]
    public void SessionEventsMessage_HasRequiredFields()
    {
        var msg = new SessionEventsMessage(
            SessionSecureId: "sess-abc",
            PayloadId: 42,
            Data: "compressed-base64-data");

        Assert.Equal("sess-abc", msg.SessionSecureId);
        Assert.Equal(42, msg.PayloadId);
        Assert.Equal("compressed-base64-data", msg.Data);
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

    [Fact]
    public void SessionEventsMessage_NegativePayloadId()
    {
        var msg = new SessionEventsMessage("sess-1", -1, "data");
        Assert.Equal(-1, msg.PayloadId);
    }

    [Fact]
    public void SessionEventsMessage_VeryLargeData()
    {
        var largeData = new string('A', 1_000_000);
        var msg = new SessionEventsMessage("sess-1", 1, largeData);
        Assert.Equal(1_000_000, msg.Data.Length);
    }

    [Fact]
    public void SessionEventsMessage_UnicodeSecureId()
    {
        // Should handle any string, even unusual ones
        var msg = new SessionEventsMessage("日本語-test", 1, "data");
        Assert.Equal("日本語-test", msg.SessionSecureId);
    }

    [Fact]
    public void SessionEventsMessage_RecordEquality()
    {
        var msg1 = new SessionEventsMessage("s", 1, "d");
        var msg2 = new SessionEventsMessage("s", 1, "d");
        Assert.Equal(msg1, msg2);
    }

    [Fact]
    public void SessionEventsMessage_RecordInequality()
    {
        var msg1 = new SessionEventsMessage("s", 1, "d");
        var msg2 = new SessionEventsMessage("s", 2, "d");
        Assert.NotEqual(msg1, msg2);
    }
}

public class BackendErrorMessageTests
{
    [Fact]
    public void BackendErrorMessage_HasRequiredFields()
    {
        var msg = new BackendErrorMessage(
            ProjectId: "123",
            Event: "NullReferenceException",
            Type: "System.NullReferenceException",
            Url: "/api/data",
            Source: "backend",
            StackTrace: "at MyApp.Service.Process()",
            Timestamp: DateTime.UtcNow,
            Payload: null,
            ServiceName: "api-server",
            ServiceVersion: "1.0.0",
            Environment: "production",
            SessionSecureId: null,
            TraceId: "trace-1",
            SpanId: "span-1");

        Assert.Equal("123", msg.ProjectId);
        Assert.Equal("api-server", msg.ServiceName);
        Assert.Equal("production", msg.Environment);
    }

    [Fact]
    public void BackendErrorMessage_AllNullableFieldsNull()
    {
        var msg = new BackendErrorMessage(
            ProjectId: null,
            Event: "Error",
            Type: "Error",
            Url: "",
            Source: "",
            StackTrace: "",
            Timestamp: DateTime.MinValue,
            Payload: null,
            ServiceName: "",
            ServiceVersion: "",
            Environment: "",
            SessionSecureId: null,
            TraceId: null,
            SpanId: null);

        Assert.Null(msg.ProjectId);
        Assert.Null(msg.SessionSecureId);
        Assert.Null(msg.TraceId);
        Assert.Null(msg.SpanId);
        Assert.Null(msg.Payload);
    }

    [Fact]
    public void BackendErrorMessage_VeryLongStackTrace()
    {
        var longTrace = string.Join("\n",
            Enumerable.Range(0, 500).Select(i => $"at Namespace{i}.Class{i}.Method{i}()"));

        var msg = new BackendErrorMessage(
            ProjectId: "1",
            Event: "StackOverflowException",
            Type: "System.StackOverflowException",
            Url: "",
            Source: "backend",
            StackTrace: longTrace,
            Timestamp: DateTime.UtcNow,
            Payload: null,
            ServiceName: "svc",
            ServiceVersion: "1.0",
            Environment: "prod",
            SessionSecureId: null,
            TraceId: null,
            SpanId: null);

        Assert.Contains("Namespace499", msg.StackTrace);
    }

    [Fact]
    public void BackendErrorMessage_SpecialCharactersInEvent()
    {
        var msg = new BackendErrorMessage(
            ProjectId: "1",
            Event: "Error: 'foo' is \"not\" a <valid> & value",
            Type: "Error",
            Url: "/api?foo=bar&baz=qux",
            Source: "backend",
            StackTrace: "",
            Timestamp: DateTime.UtcNow,
            Payload: "{\"key\": \"value with 'quotes'\"}",
            ServiceName: "svc",
            ServiceVersion: "1.0",
            Environment: "dev",
            SessionSecureId: null,
            TraceId: null,
            SpanId: null);

        Assert.Contains("'foo'", msg.Event);
        Assert.Contains("&", msg.Url);
    }

    [Fact]
    public void BackendErrorMessage_RecordEquality()
    {
        var ts = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var msg1 = new BackendErrorMessage("1", "e", "t", "", "", "", ts, null, "s", "v", "p", null, null, null);
        var msg2 = new BackendErrorMessage("1", "e", "t", "", "", "", ts, null, "s", "v", "p", null, null, null);
        Assert.Equal(msg1, msg2);
    }
}

public class MetricsMessageTests
{
    [Fact]
    public void MetricsMessage_HasRequiredFields()
    {
        var msg = new MetricsMessage(
            SessionSecureId: "sess-1",
            Name: "LCP",
            Value: 2.5,
            Category: "web-vital",
            Timestamp: DateTime.UtcNow,
            Tags: new Dictionary<string, string> { ["page"] = "/home" });

        Assert.Equal("LCP", msg.Name);
        Assert.Equal(2.5, msg.Value);
        Assert.Single(msg.Tags!);
    }

    [Fact]
    public void MetricsMessage_NullableTagsAllowed()
    {
        var msg = new MetricsMessage(
            SessionSecureId: "sess-1",
            Name: "request_duration",
            Value: 150.0,
            Category: null,
            Timestamp: DateTime.UtcNow,
            Tags: null);

        Assert.Null(msg.Tags);
        Assert.Null(msg.Category);
    }

    [Fact]
    public void MetricsMessage_ZeroValue()
    {
        var msg = new MetricsMessage("s", "counter", 0.0, null, DateTime.UtcNow, null);
        Assert.Equal(0.0, msg.Value);
    }

    [Fact]
    public void MetricsMessage_NegativeValue()
    {
        var msg = new MetricsMessage("s", "balance", -100.50, null, DateTime.UtcNow, null);
        Assert.Equal(-100.50, msg.Value);
    }

    [Fact]
    public void MetricsMessage_PositiveInfinity()
    {
        var msg = new MetricsMessage("s", "infinity", double.PositiveInfinity, null, DateTime.UtcNow, null);
        Assert.Equal(double.PositiveInfinity, msg.Value);
    }

    [Fact]
    public void MetricsMessage_NaN()
    {
        var msg = new MetricsMessage("s", "nan", double.NaN, null, DateTime.UtcNow, null);
        Assert.True(double.IsNaN(msg.Value));
    }

    [Fact]
    public void MetricsMessage_EmptyTags()
    {
        var msg = new MetricsMessage("s", "m", 1.0, null, DateTime.UtcNow,
            new Dictionary<string, string>());
        Assert.NotNull(msg.Tags);
        Assert.Empty(msg.Tags!);
    }

    [Fact]
    public void MetricsMessage_ManyTags()
    {
        var tags = Enumerable.Range(0, 100)
            .ToDictionary(i => $"key{i}", i => $"val{i}");

        var msg = new MetricsMessage("s", "m", 1.0, null, DateTime.UtcNow, tags);
        Assert.Equal(100, msg.Tags!.Count);
    }

    [Fact]
    public void MetricsMessage_MinDateTimestamp()
    {
        var msg = new MetricsMessage("s", "m", 1.0, null, DateTime.MinValue, null);
        Assert.Equal(DateTime.MinValue, msg.Timestamp);
    }

    [Fact]
    public void MetricsMessage_MaxDateTimestamp()
    {
        var msg = new MetricsMessage("s", "m", 1.0, null, DateTime.MaxValue, null);
        Assert.Equal(DateTime.MaxValue, msg.Timestamp);
    }

    [Fact]
    public void MetricsMessage_RecordEquality()
    {
        var ts = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var msg1 = new MetricsMessage("s", "m", 1.0, "c", ts, null);
        var msg2 = new MetricsMessage("s", "m", 1.0, "c", ts, null);
        Assert.Equal(msg1, msg2);
    }

    [Fact]
    public void MetricsMessage_RecordInequality_DifferentValue()
    {
        var ts = DateTime.UtcNow;
        var msg1 = new MetricsMessage("s", "m", 1.0, null, ts, null);
        var msg2 = new MetricsMessage("s", "m", 2.0, null, ts, null);
        Assert.NotEqual(msg1, msg2);
    }
}
