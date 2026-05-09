using System.Text.Json;
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

    public async Task ProducePushPayloadAsync(string sessionSecureId, long payloadId, string events,
        string messages, string resources, string? webSocketEvents,
        List<ErrorObjectInput?> errors, bool? isBeacon, bool? hasSessionUnloaded,
        string? highlightLogs, CancellationToken ct)
    {
        // Replay events + supporting payload → SessionEvents topic for the rrweb
        // chunk processor. This message intentionally only carries (SessionSecureId,
        // PayloadId, Data) because that's all SessionEventsConsumer reads —
        // serialize the events string as the Data body.
        var sessionMsg = new
        {
            SessionSecureId = sessionSecureId,
            PayloadId = payloadId,
            Data = events,
        };
        await _producer.ProduceAsync(KafkaTopics.SessionEvents, sessionSecureId, sessionMsg, ct);

        // Each frontend error → FrontendErrors topic so FrontendErrorsConsumer can
        // group them into ClickHouse error_objects. Errors are dropped on the
        // floor by SessionEventsConsumer; routing them separately is HOL-15's fix.
        foreach (var err in errors)
        {
            if (err is null) continue;
            var frontendMsg = new
            {
                SessionSecureId = sessionSecureId,
                Event = err.Event,
                Type = err.Type,
                Url = err.Url,
                Source = err.Source,
                LineNumber = err.LineNumber,
                ColumnNumber = err.ColumnNumber,
                StackTrace = JsonSerializer.Serialize(err.StackTrace),
                Timestamp = err.Timestamp,
                Payload = err.Payload,
            };
            await _producer.ProduceAsync(KafkaTopics.FrontendErrors, sessionSecureId, frontendMsg, ct);
        }

        // _ = messages; _ = resources; _ = webSocketEvents; _ = isBeacon;
        // _ = hasSessionUnloaded; _ = highlightLogs;
        // The remaining fields are stored alongside session events in the Go
        // pipeline but aren't yet consumed in .NET. Tracked separately under
        // HoldFast Forensic Ingest MVP — capturing them when the consumer-side
        // contract is widened.
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
