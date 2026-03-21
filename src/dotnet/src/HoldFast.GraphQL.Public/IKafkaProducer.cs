using HoldFast.GraphQL.Public.InputTypes;

namespace HoldFast.GraphQL.Public;

/// <summary>
/// Kafka producer abstraction for the public graph.
/// Data ingestion mutations forward payloads to Kafka for async processing.
/// </summary>
public interface IKafkaProducer
{
    Task ProduceSessionEventsAsync(string sessionSecureId, long payloadId, string data, CancellationToken ct);
    Task ProduceBackendErrorAsync(string? projectId, BackendErrorObjectInput error, CancellationToken ct);
    Task ProduceMetricAsync(MetricInput metric, CancellationToken ct);
    Task ProduceLogAsync(LogInput log, CancellationToken ct);
    Task ProduceTraceAsync(TraceInput trace, CancellationToken ct);
}
