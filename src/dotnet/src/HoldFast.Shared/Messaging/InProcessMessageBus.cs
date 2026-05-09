using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace HoldFast.Shared.Messaging;

/// <summary>
/// Single-process message bus backed by <see cref="Channel{T}"/>. Replaces
/// Kafka for hobby/lean self-hosted deployments (HOL-23).
///
/// One unbounded channel per topic, lazily created on first publish or
/// subscribe. Producers write (key, json) pairs; consumers read them in
/// FIFO order. Multiple subscribers on the same topic compete for messages
/// (fan-in), so the same SessionEventsConsumer-as-singleton + worker pattern
/// the Kafka version used keeps working unchanged.
///
/// Tradeoffs vs. Kafka:
/// - No durability — messages live in memory; a backend restart drops the
///   in-flight queue. Acceptable at hobby scale; the API responses to
///   pushPayload are still 200, so SDK-side retries cover transient loss.
/// - No replay — no offsets, no consumer groups, no rebalancing.
/// - Single-node only — producer and consumer must be in the same process.
///   The .NET backend's "all-in-one" runtime mode satisfies this.
///
/// If a deployment outgrows this, swap an alternative IMessageBus
/// implementation back in (Kafka, Redis Streams, etc).
/// </summary>
public class InProcessMessageBus : IMessageBus
{
    private readonly ConcurrentDictionary<string, Channel<(string Key, string Value)>> _channels = new();
    private readonly ILogger<InProcessMessageBus> _logger;

    public InProcessMessageBus(ILogger<InProcessMessageBus> logger)
    {
        _logger = logger;
    }

    private Channel<(string Key, string Value)> GetOrCreate(string topic) =>
        _channels.GetOrAdd(topic, _ =>
        {
            _logger.LogDebug("In-process message bus: creating channel for topic {Topic}", topic);
            // Unbounded so producers never block — at hobby scale the queue
            // depth stays trivial. If you observe unbounded growth here, that's
            // a sign you're outgrowing the in-process bus.
            return Channel.CreateUnbounded<(string, string)>(new UnboundedChannelOptions
            {
                SingleReader = false,  // multiple consumer instances OK
                SingleWriter = false,  // multiple producer call sites OK
            });
        });

    public async Task PublishAsync<T>(string topic, string key, T value, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(value);
        var ch = GetOrCreate(topic);
        await ch.Writer.WriteAsync((key, json), ct);
    }

    public async IAsyncEnumerable<(string Key, string Value)> SubscribeAsync(
        string topic,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var ch = GetOrCreate(topic);
        await foreach (var item in ch.Reader.ReadAllAsync(ct))
            yield return item;
    }
}
