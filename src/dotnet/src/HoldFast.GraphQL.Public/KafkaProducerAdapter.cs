using HoldFast.GraphQL.Public.InputTypes;
using HoldFast.Shared.Kafka;

namespace HoldFast.GraphQL.Public;

/// <summary>
/// Implements IKafkaProducer by forwarding to the shared KafkaProducerService.
/// </summary>
public class KafkaProducerAdapter : IKafkaProducer
{
    private readonly KafkaProducerService _producer;

    public KafkaProducerAdapter(KafkaProducerService producer)
    {
        _producer = producer;
    }

    public Task ProduceSessionEventsAsync(string sessionSecureId, long payloadId, string data, CancellationToken ct)
    {
        var message = new { SessionSecureId = sessionSecureId, PayloadId = payloadId, Data = data };
        return _producer.ProduceAsync(KafkaTopics.SessionEvents, sessionSecureId, message, ct);
    }

    public Task ProduceBackendErrorAsync(string? projectId, BackendErrorObjectInput error, CancellationToken ct)
    {
        return _producer.ProduceAsync(KafkaTopics.BackendErrors, projectId ?? "unknown", error, ct);
    }

    public Task ProduceMetricAsync(MetricInput metric, CancellationToken ct)
    {
        return _producer.ProduceAsync(KafkaTopics.Metrics, metric.SessionSecureId, metric, ct);
    }

    public Task ProduceLogAsync(LogInput log, CancellationToken ct)
    {
        return _producer.ProduceAsync(KafkaTopics.Logs, log.TraceId, log, ct);
    }

    public Task ProduceTraceAsync(TraceInput trace, CancellationToken ct)
    {
        return _producer.ProduceAsync(KafkaTopics.Traces, trace.TraceId, trace, ct);
    }
}
