namespace HoldFast.Shared.Messaging;

/// <summary>
/// Pub/sub message bus the worker hosted services use to decouple ingest from
/// processing. Replaces Kafka in the hobby/lean architecture (HOL-23) — the
/// in-process Channel-backed implementation handles single-node self-hosted
/// scale without a broker, JVM, or zookeeper container.
///
/// The interface intentionally mirrors the small slice of Kafka semantics we
/// were actually using: per-topic JSON messages with a string key, no
/// partition control, no consumer-group rebalancing. If you outgrow the
/// in-process implementation, swap in a Kafka-backed implementation that
/// honors the same shape.
/// </summary>
public interface IMessageBus
{
    /// <summary>
    /// Publish a value to a topic. Value is JSON-serialized.
    /// In the in-process implementation this is non-blocking (channel write).
    /// </summary>
    Task PublishAsync<T>(string topic, string key, T value, CancellationToken ct);

    /// <summary>
    /// Subscribe to a topic and receive messages as (key, json-body) pairs.
    /// Multiple consumers on the same topic will fan in (each message goes
    /// to exactly one of them) — matches Kafka consumer-group semantics with
    /// a single-partition topic.
    /// </summary>
    IAsyncEnumerable<(string Key, string Value)> SubscribeAsync(
        string topic, CancellationToken ct);
}
