using HoldFast.Domain.Entities;
using HoldFast.GraphQL.Public;
using HoldFast.GraphQL.Public.InputTypes;
using HoldFast.Shared.Kafka;

namespace HoldFast.GraphQL.Tests;

public class PublicQueryTests
{
    [Fact]
    public void Ignore_ReturnsNull()
    {
        var query = new PublicQuery();
        Assert.Null(query.Ignore(42));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void Ignore_ReturnsNull_ForAnyInput(int id)
    {
        var query = new PublicQuery();
        Assert.Null(query.Ignore(id));
    }

    [Fact]
    public void SamplingConfig_DefaultsToEmpty()
    {
        var config = new SamplingConfig();
        Assert.Null(config.Spans);
        Assert.Null(config.Logs);
    }

    [Fact]
    public void SamplingConfig_WithSpans()
    {
        var config = new SamplingConfig(
            Spans: [new SpanSamplingConfig(SamplingRatio: 10)],
            Logs: null);
        Assert.Single(config.Spans!);
        Assert.Null(config.Logs);
    }

    [Fact]
    public void SpanSamplingConfig_DefaultSamplingRatio()
    {
        var config = new SpanSamplingConfig(SamplingRatio: 10);
        Assert.Equal(10, config.SamplingRatio);
        Assert.Null(config.Name);
        Assert.Null(config.Attributes);
        Assert.Null(config.Events);
    }

    [Fact]
    public void SpanSamplingConfig_WithAllFields()
    {
        var config = new SpanSamplingConfig(
            Name: new MatchConfig(RegexValue: "http.*"),
            Attributes: [new AttributeMatchConfig(
                new MatchConfig(MatchValue: "http.method"),
                new MatchConfig(MatchValue: "GET"))],
            Events: [new SpanEventMatchConfig(
                Name: new MatchConfig(MatchValue: "exception"),
                Attributes: [])],
            SamplingRatio: 5);

        Assert.Equal("http.*", config.Name!.RegexValue);
        Assert.Single(config.Attributes!);
        Assert.Single(config.Events!);
        Assert.Equal(5, config.SamplingRatio);
    }

    [Fact]
    public void LogSamplingConfig_DefaultSamplingRatio()
    {
        var config = new LogSamplingConfig(SamplingRatio: 5);
        Assert.Equal(5, config.SamplingRatio);
        Assert.Null(config.Message);
        Assert.Null(config.SeverityText);
    }

    [Fact]
    public void LogSamplingConfig_WithMessageFilter()
    {
        var config = new LogSamplingConfig(
            Message: new MatchConfig(RegexValue: "health.*check"),
            SeverityText: new MatchConfig(MatchValue: "DEBUG"),
            SamplingRatio: 100);

        Assert.Equal("health.*check", config.Message!.RegexValue);
        Assert.Equal("DEBUG", config.SeverityText!.MatchValue);
    }

    [Fact]
    public void MatchConfig_BothFieldsNull()
    {
        var config = new MatchConfig();
        Assert.Null(config.RegexValue);
        Assert.Null(config.MatchValue);
    }

    [Fact]
    public void MatchConfig_WithRegex()
    {
        var config = new MatchConfig(RegexValue: "^error");
        Assert.Equal("^error", config.RegexValue);
        Assert.Null(config.MatchValue);
    }

    [Fact]
    public void MatchConfig_WithMatchValue()
    {
        var config = new MatchConfig(MatchValue: 42);
        Assert.Null(config.RegexValue);
        Assert.Equal(42, config.MatchValue);
    }
}

public class InputTypeTests
{
    [Fact]
    public void InitializeSessionInput_AllFieldsPopulated()
    {
        var input = new InitializeSessionInput(
            SessionSecureId: "abc-123",
            SessionKey: null,
            OrganizationVerboseId: "1a",
            EnableStrictPrivacy: false,
            EnableRecordingNetworkContents: true,
            ClientVersion: "1.0.0",
            FirstloadVersion: "1.0.0",
            ClientConfig: "{}",
            Environment: "production",
            AppVersion: "2.0",
            ServiceName: "web",
            Fingerprint: "fp-123",
            ClientId: "client-456",
            NetworkRecordingDomains: ["example.com"],
            DisableSessionRecording: false,
            PrivacySetting: "default");

        Assert.Equal("abc-123", input.SessionSecureId);
        Assert.Equal("1a", input.OrganizationVerboseId);
        Assert.True(input.EnableRecordingNetworkContents);
        Assert.Single(input.NetworkRecordingDomains!);
    }

    [Fact]
    public void InitializeSessionInput_MinimalFields()
    {
        var input = new InitializeSessionInput(
            SessionSecureId: "s",
            SessionKey: null,
            OrganizationVerboseId: "1",
            EnableStrictPrivacy: false,
            EnableRecordingNetworkContents: false,
            ClientVersion: "",
            FirstloadVersion: "",
            ClientConfig: "",
            Environment: "",
            AppVersion: null,
            ServiceName: null,
            Fingerprint: "",
            ClientId: "",
            NetworkRecordingDomains: null,
            DisableSessionRecording: null,
            PrivacySetting: null);

        Assert.Null(input.SessionKey);
        Assert.Null(input.AppVersion);
        Assert.Null(input.NetworkRecordingDomains);
    }

    [Fact]
    public void InitializeSessionInput_EmptyDomainsList()
    {
        var input = new InitializeSessionInput(
            SessionSecureId: "s",
            SessionKey: null,
            OrganizationVerboseId: "1",
            EnableStrictPrivacy: false,
            EnableRecordingNetworkContents: false,
            ClientVersion: "",
            FirstloadVersion: "",
            ClientConfig: "",
            Environment: "",
            AppVersion: null,
            ServiceName: null,
            Fingerprint: "",
            ClientId: "",
            NetworkRecordingDomains: [],
            DisableSessionRecording: null,
            PrivacySetting: null);

        Assert.NotNull(input.NetworkRecordingDomains);
        Assert.Empty(input.NetworkRecordingDomains!);
    }

    [Fact]
    public void ErrorObjectInput_RequiredFields()
    {
        var input = new ErrorObjectInput(
            Event: "TypeError: undefined is not a function",
            Type: "TypeError",
            Url: "https://app.example.com/page",
            Source: "frontend",
            LineNumber: 42,
            ColumnNumber: 10,
            StackTrace: [new StackFrameInput("doSomething", "app.js", 42, 10, false, false, null)],
            Timestamp: DateTime.UtcNow,
            Payload: null);

        Assert.Equal("TypeError", input.Type);
        Assert.Single(input.StackTrace);
        Assert.Equal("doSomething", input.StackTrace[0].FunctionName);
    }

    [Fact]
    public void ErrorObjectInput_EmptyStackTrace()
    {
        var input = new ErrorObjectInput(
            Event: "Error",
            Type: "Error",
            Url: "",
            Source: "",
            LineNumber: 0,
            ColumnNumber: 0,
            StackTrace: [],
            Timestamp: DateTime.MinValue,
            Payload: null);

        Assert.Empty(input.StackTrace);
        Assert.Equal(DateTime.MinValue, input.Timestamp);
    }

    [Fact]
    public void ErrorObjectInput_LargeStackTrace()
    {
        var frames = Enumerable.Range(0, 100)
            .Select(i => new StackFrameInput($"func{i}", $"file{i}.js", i, 0, false, false, null))
            .ToList();

        var input = new ErrorObjectInput(
            Event: "Deep stack",
            Type: "Error",
            Url: "",
            Source: "",
            LineNumber: 0,
            ColumnNumber: 0,
            StackTrace: frames,
            Timestamp: DateTime.UtcNow,
            Payload: null);

        Assert.Equal(100, input.StackTrace.Count);
    }

    [Fact]
    public void StackFrameInput_AllNullable()
    {
        var frame = new StackFrameInput(null, null, null, null, null, null, null);
        Assert.Null(frame.FunctionName);
        Assert.Null(frame.FileName);
        Assert.Null(frame.LineNumber);
    }

    [Fact]
    public void StackFrameInput_EvalFrame()
    {
        var frame = new StackFrameInput("eval", null, null, null, true, false, "eval code");
        Assert.True(frame.IsEval);
        Assert.False(frame.IsNative);
        Assert.Equal("eval code", frame.Source);
    }

    [Fact]
    public void MetricInput_RequiredFields()
    {
        var input = new MetricInput(
            SessionSecureId: "sess-1",
            SpanId: null,
            ParentSpanId: null,
            TraceId: null,
            Group: "performance",
            Name: "LCP",
            Value: 2.5,
            Category: "web-vital",
            Timestamp: DateTime.UtcNow,
            Tags: [new MetricTag("page", "/home")]);

        Assert.Equal("LCP", input.Name);
        Assert.Equal(2.5, input.Value);
        Assert.Single(input.Tags!);
    }

    [Fact]
    public void MetricInput_ZeroValue()
    {
        var input = new MetricInput(
            SessionSecureId: "s",
            SpanId: null, ParentSpanId: null, TraceId: null,
            Group: null, Name: "counter", Value: 0.0,
            Category: null, Timestamp: DateTime.UtcNow, Tags: null);

        Assert.Equal(0.0, input.Value);
    }

    [Fact]
    public void MetricInput_NegativeValue()
    {
        var input = new MetricInput(
            SessionSecureId: "s",
            SpanId: null, ParentSpanId: null, TraceId: null,
            Group: null, Name: "temperature", Value: -40.0,
            Category: null, Timestamp: DateTime.UtcNow, Tags: null);

        Assert.Equal(-40.0, input.Value);
    }

    [Fact]
    public void MetricInput_ExtremeValue()
    {
        var input = new MetricInput(
            SessionSecureId: "s",
            SpanId: null, ParentSpanId: null, TraceId: null,
            Group: null, Name: "huge", Value: double.MaxValue,
            Category: null, Timestamp: DateTime.UtcNow, Tags: null);

        Assert.Equal(double.MaxValue, input.Value);
    }

    [Fact]
    public void MetricInput_ManyTags()
    {
        var tags = Enumerable.Range(0, 50)
            .Select(i => new MetricTag($"key{i}", $"val{i}"))
            .ToList();

        var input = new MetricInput(
            SessionSecureId: "s",
            SpanId: null, ParentSpanId: null, TraceId: null,
            Group: null, Name: "tagged", Value: 1.0,
            Category: null, Timestamp: DateTime.UtcNow, Tags: tags);

        Assert.Equal(50, input.Tags!.Count);
    }

    [Fact]
    public void BackendErrorObjectInput_WithService()
    {
        var input = new BackendErrorObjectInput(
            SessionSecureId: null,
            RequestId: "req-1",
            TraceId: "trace-1",
            SpanId: "span-1",
            LogCursor: null,
            Event: "NullReferenceException",
            Type: "System.NullReferenceException",
            Url: "/api/data",
            Source: "backend",
            StackTrace: "at MyApp.Service.Process()",
            Timestamp: DateTime.UtcNow,
            Payload: null,
            Service: new ServiceInput("api-server", "1.2.3"),
            Environment: "production");

        Assert.Equal("api-server", input.Service.Name);
        Assert.Equal("1.2.3", input.Service.Version);
    }

    [Fact]
    public void BackendErrorObjectInput_AllIdentifiersNull()
    {
        var input = new BackendErrorObjectInput(
            SessionSecureId: null,
            RequestId: null,
            TraceId: null,
            SpanId: null,
            LogCursor: null,
            Event: "Error",
            Type: "Error",
            Url: "",
            Source: "",
            StackTrace: "",
            Timestamp: DateTime.UtcNow,
            Payload: null,
            Service: new ServiceInput("svc", "0.0.0"),
            Environment: "test");

        Assert.Null(input.SessionSecureId);
        Assert.Null(input.RequestId);
        Assert.Null(input.TraceId);
        Assert.Null(input.SpanId);
    }

    [Fact]
    public void AddSessionFeedbackInput_Fields()
    {
        var ts = new DateTime(2026, 3, 20, 12, 0, 0, DateTimeKind.Utc);
        var input = new AddSessionFeedbackInput(
            SessionSecureId: "sess-1",
            UserName: "Scott",
            UserEmail: "scott@example.com",
            Verbatim: "The page is slow",
            Timestamp: ts);

        Assert.Equal("Scott", input.UserName);
        Assert.Equal(ts, input.Timestamp);
    }

    [Fact]
    public void AddSessionFeedbackInput_AnonymousFeedback()
    {
        var input = new AddSessionFeedbackInput(
            SessionSecureId: "sess-1",
            UserName: null,
            UserEmail: null,
            Verbatim: "It crashed",
            Timestamp: DateTime.UtcNow);

        Assert.Null(input.UserName);
        Assert.Null(input.UserEmail);
        Assert.Equal("It crashed", input.Verbatim);
    }

    [Fact]
    public void InitializeSessionResponse_Fields()
    {
        var response = new InitializeSessionResponse("secure-abc", 42);
        Assert.Equal("secure-abc", response.SecureId);
        Assert.Equal(42, response.ProjectId);
    }

    [Fact]
    public void IdentifySessionInput_Fields()
    {
        var input = new IdentifySessionInput("sess-1", "user@example.com", new { name = "Test" });
        Assert.Equal("sess-1", input.SessionSecureId);
        Assert.Equal("user@example.com", input.UserIdentifier);
        Assert.NotNull(input.UserObject);
    }

    [Fact]
    public void IdentifySessionInput_NullUserObject()
    {
        var input = new IdentifySessionInput("sess-1", "anon", null);
        Assert.Null(input.UserObject);
    }

    [Fact]
    public void AddSessionPropertiesInput_Fields()
    {
        var input = new AddSessionPropertiesInput("sess-1", new { tier = "premium" });
        Assert.Equal("sess-1", input.SessionSecureId);
        Assert.NotNull(input.PropertiesObject);
    }

    [Fact]
    public void ServiceInput_Fields()
    {
        var svc = new ServiceInput("my-api", "3.2.1");
        Assert.Equal("my-api", svc.Name);
        Assert.Equal("3.2.1", svc.Version);
    }
}

public class VerboseIdTests
{
    [Fact]
    public void VerboseId_ReturnsNonEmpty()
    {
        var project = new Project { Name = "test" };
        typeof(BaseEntity).GetProperty("Id")!.SetValue(project, 1);
        var verboseId = project.VerboseId;
        Assert.NotNull(verboseId);
        Assert.NotEmpty(verboseId);
    }

    [Fact]
    public void VerboseId_MinLength8()
    {
        // HashID configured with MinLength=8
        var project = new Project { Name = "test" };
        typeof(BaseEntity).GetProperty("Id")!.SetValue(project, 1);
        Assert.True(project.VerboseId.Length >= 8);
    }

    [Fact]
    public void VerboseId_OnlyLowercaseAlphanumeric()
    {
        var project = new Project { Name = "test" };
        typeof(BaseEntity).GetProperty("Id")!.SetValue(project, 42);
        var verboseId = project.VerboseId;
        Assert.Matches("^[a-z0-9]+$", verboseId);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(999999)]
    public void VerboseId_RoundTrips(int id)
    {
        var project = new Project { Name = "test" };
        typeof(BaseEntity).GetProperty("Id")!.SetValue(project, id);
        var verboseId = project.VerboseId;
        var decoded = Project.FromVerboseId(verboseId);
        Assert.Equal(id, decoded);
    }

    [Fact]
    public void VerboseId_DifferentIdsProduceDifferentHashes()
    {
        var p1 = new Project { Name = "a" };
        var p2 = new Project { Name = "b" };
        typeof(BaseEntity).GetProperty("Id")!.SetValue(p1, 1);
        typeof(BaseEntity).GetProperty("Id")!.SetValue(p2, 2);
        Assert.NotEqual(p1.VerboseId, p2.VerboseId);
    }

    [Fact]
    public void FromVerboseId_PlainInteger()
    {
        // Legacy clients send plain integer IDs
        Assert.Equal(42, Project.FromVerboseId("42"));
        Assert.Equal(1, Project.FromVerboseId("1"));
        Assert.Equal(0, Project.FromVerboseId("0"));
    }

    [Fact]
    public void FromVerboseId_InvalidHash_Throws()
    {
        Assert.Throws<ArgumentException>(() => Project.FromVerboseId("!!!invalid!!!"));
    }

    [Fact]
    public void FromVerboseId_EmptyString_Throws()
    {
        // Empty string is not a valid integer or hashid
        Assert.ThrowsAny<Exception>(() => Project.FromVerboseId(""));
    }

    [Fact]
    public void FromVerboseId_UppercaseNotInAlphabet_Throws()
    {
        // HashID alphabet is lowercase only
        Assert.ThrowsAny<Exception>(() => Project.FromVerboseId("ABCDEFGH"));
    }

    [Fact]
    public void VerboseId_LargeId()
    {
        var project = new Project { Name = "test" };
        typeof(BaseEntity).GetProperty("Id")!.SetValue(project, int.MaxValue);
        var verboseId = project.VerboseId;
        Assert.NotEmpty(verboseId);
        Assert.Equal(int.MaxValue, Project.FromVerboseId(verboseId));
    }

    [Fact]
    public void VerboseId_ConsistentAcrossCalls()
    {
        var project = new Project { Name = "test" };
        typeof(BaseEntity).GetProperty("Id")!.SetValue(project, 123);
        var first = project.VerboseId;
        var second = project.VerboseId;
        Assert.Equal(first, second);
    }
}

public class KafkaTopicTests
{
    [Fact]
    public void AllTopicsAreDefined()
    {
        Assert.Equal("session-events", KafkaTopics.SessionEvents);
        Assert.Equal("backend-errors", KafkaTopics.BackendErrors);
        Assert.Equal("metrics", KafkaTopics.Metrics);
        Assert.Equal("session-processing", KafkaTopics.SessionProcessing);
        Assert.Equal("error-grouping", KafkaTopics.ErrorGrouping);
        Assert.Equal("alert-evaluation", KafkaTopics.AlertEvaluation);
    }

    [Fact]
    public void TopicNames_AreKebabCase()
    {
        var topics = new[]
        {
            KafkaTopics.SessionEvents,
            KafkaTopics.BackendErrors,
            KafkaTopics.Metrics,
            KafkaTopics.SessionProcessing,
            KafkaTopics.ErrorGrouping,
            KafkaTopics.AlertEvaluation,
        };

        foreach (var topic in topics)
        {
            Assert.Matches("^[a-z]+(-[a-z]+)*$", topic);
        }
    }

    [Fact]
    public void TopicNames_AllUnique()
    {
        var topics = new[]
        {
            KafkaTopics.SessionEvents,
            KafkaTopics.BackendErrors,
            KafkaTopics.Metrics,
            KafkaTopics.SessionProcessing,
            KafkaTopics.ErrorGrouping,
            KafkaTopics.AlertEvaluation,
        };

        Assert.Equal(topics.Length, topics.Distinct().Count());
    }
}
