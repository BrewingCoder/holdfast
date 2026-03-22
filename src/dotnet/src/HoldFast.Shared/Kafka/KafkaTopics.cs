namespace HoldFast.Shared.Kafka;

/// <summary>
/// Kafka topic constants matching the Go backend's topic names.
/// </summary>
public static class KafkaTopics
{
    public const string SessionEvents = "session-events";
    public const string BackendErrors = "backend-errors";
    public const string Metrics = "metrics";
    public const string Logs = "logs";
    public const string Traces = "traces";
    public const string SessionProcessing = "session-processing";
    public const string ErrorGrouping = "error-grouping";
    public const string AlertEvaluation = "alert-evaluation";
}
