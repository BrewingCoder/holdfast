using HoldFast.Shared.Kafka;

namespace HoldFast.Shared.Tests.Kafka;

/// <summary>
/// Verify Kafka topic constants match the Go backend's topic names exactly.
/// These are critical integration contracts — a typo means messages go to the wrong topic.
/// </summary>
public class KafkaTopicsTests
{
    [Fact]
    public void SessionEvents_MatchesGoBackend()
    {
        Assert.Equal("session-events", KafkaTopics.SessionEvents);
    }

    [Fact]
    public void BackendErrors_MatchesGoBackend()
    {
        Assert.Equal("backend-errors", KafkaTopics.BackendErrors);
    }

    [Fact]
    public void Metrics_MatchesGoBackend()
    {
        Assert.Equal("metrics", KafkaTopics.Metrics);
    }

    [Fact]
    public void Logs_MatchesGoBackend()
    {
        Assert.Equal("logs", KafkaTopics.Logs);
    }

    [Fact]
    public void Traces_MatchesGoBackend()
    {
        Assert.Equal("traces", KafkaTopics.Traces);
    }

    [Fact]
    public void SessionProcessing_MatchesGoBackend()
    {
        Assert.Equal("session-processing", KafkaTopics.SessionProcessing);
    }

    [Fact]
    public void ErrorGrouping_MatchesGoBackend()
    {
        Assert.Equal("error-grouping", KafkaTopics.ErrorGrouping);
    }

    [Fact]
    public void AlertEvaluation_MatchesGoBackend()
    {
        Assert.Equal("alert-evaluation", KafkaTopics.AlertEvaluation);
    }

    // ── Format validation ─────────────────────────────────────────────

    [Theory]
    [InlineData(nameof(KafkaTopics.SessionEvents))]
    [InlineData(nameof(KafkaTopics.BackendErrors))]
    [InlineData(nameof(KafkaTopics.Metrics))]
    [InlineData(nameof(KafkaTopics.Logs))]
    [InlineData(nameof(KafkaTopics.Traces))]
    [InlineData(nameof(KafkaTopics.SessionProcessing))]
    [InlineData(nameof(KafkaTopics.ErrorGrouping))]
    [InlineData(nameof(KafkaTopics.AlertEvaluation))]
    public void AllTopics_AreLowerKebabCase(string fieldName)
    {
        var value = GetTopicValue(fieldName);
        Assert.NotNull(value);
        Assert.NotEmpty(value);
        Assert.Matches("^[a-z][a-z0-9-]*$", value);
    }

    [Theory]
    [InlineData(nameof(KafkaTopics.SessionEvents))]
    [InlineData(nameof(KafkaTopics.BackendErrors))]
    [InlineData(nameof(KafkaTopics.Metrics))]
    [InlineData(nameof(KafkaTopics.Logs))]
    [InlineData(nameof(KafkaTopics.Traces))]
    [InlineData(nameof(KafkaTopics.SessionProcessing))]
    [InlineData(nameof(KafkaTopics.ErrorGrouping))]
    [InlineData(nameof(KafkaTopics.AlertEvaluation))]
    public void AllTopics_DoNotStartOrEndWithHyphen(string fieldName)
    {
        var value = GetTopicValue(fieldName);
        Assert.False(value.StartsWith('-'), $"Topic '{value}' starts with hyphen");
        Assert.False(value.EndsWith('-'), $"Topic '{value}' ends with hyphen");
    }

    [Theory]
    [InlineData(nameof(KafkaTopics.SessionEvents))]
    [InlineData(nameof(KafkaTopics.BackendErrors))]
    [InlineData(nameof(KafkaTopics.Metrics))]
    [InlineData(nameof(KafkaTopics.Logs))]
    [InlineData(nameof(KafkaTopics.Traces))]
    [InlineData(nameof(KafkaTopics.SessionProcessing))]
    [InlineData(nameof(KafkaTopics.ErrorGrouping))]
    [InlineData(nameof(KafkaTopics.AlertEvaluation))]
    public void AllTopics_DoNotContainDoubleHyphens(string fieldName)
    {
        var value = GetTopicValue(fieldName);
        Assert.DoesNotContain("--", value);
    }

    [Fact]
    public void AllTopics_AreUnique()
    {
        var topics = new[]
        {
            KafkaTopics.SessionEvents,
            KafkaTopics.BackendErrors,
            KafkaTopics.Metrics,
            KafkaTopics.Logs,
            KafkaTopics.Traces,
            KafkaTopics.SessionProcessing,
            KafkaTopics.ErrorGrouping,
            KafkaTopics.AlertEvaluation,
        };

        Assert.Equal(topics.Length, topics.Distinct().Count());
    }

    [Fact]
    public void TopicCount_IsEight()
    {
        // Guard against adding a topic constant without tests
        var fields = typeof(KafkaTopics)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .ToArray();

        Assert.Equal(8, fields.Length);
    }

    // ── No whitespace or hidden characters ────────────────────────────

    [Theory]
    [InlineData(nameof(KafkaTopics.SessionEvents))]
    [InlineData(nameof(KafkaTopics.BackendErrors))]
    [InlineData(nameof(KafkaTopics.Metrics))]
    [InlineData(nameof(KafkaTopics.Logs))]
    [InlineData(nameof(KafkaTopics.Traces))]
    [InlineData(nameof(KafkaTopics.SessionProcessing))]
    [InlineData(nameof(KafkaTopics.ErrorGrouping))]
    [InlineData(nameof(KafkaTopics.AlertEvaluation))]
    public void AllTopics_ContainNoWhitespace(string fieldName)
    {
        var value = GetTopicValue(fieldName);
        Assert.DoesNotMatch(@"\s", value);
    }

    private static string GetTopicValue(string fieldName)
    {
        var field = typeof(KafkaTopics).GetField(fieldName,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Assert.NotNull(field);
        return (string)field!.GetValue(null)!;
    }
}
