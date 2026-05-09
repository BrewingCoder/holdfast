using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace HoldFast.Shared.Messaging;

/// <summary>
/// Base consumer that reads JSON-serialized messages from an
/// <see cref="IMessageBus"/> topic and dispatches them to a
/// strongly-typed <see cref="ProcessAsync"/> implementation.
///
/// Replaces the Kafka-specific KafkaConsumerService base class (HOL-23).
/// Subclasses just specify the topic and group identifier; the consume loop
/// + JSON deserialization is shared.
/// </summary>
public abstract class MessageConsumerBase<T> where T : class
{
    private readonly IMessageBus _bus;
    private readonly ILogger _logger;
    private readonly string _topic;
    private readonly string _groupId;

    protected MessageConsumerBase(
        IMessageBus bus,
        string topic,
        string groupId,
        ILogger logger)
    {
        _bus = bus;
        _topic = topic;
        _groupId = groupId;
        _logger = logger;
    }

    /// <summary>
    /// Process a single consumed message. Implementations define the business logic.
    /// </summary>
    protected abstract Task ProcessAsync(string key, T value, CancellationToken ct);

    /// <summary>
    /// Run the consume loop. Call from BackgroundService.ExecuteAsync.
    /// </summary>
    public async Task ConsumeLoopAsync(CancellationToken ct)
    {
        _logger.LogInformation(
            "In-process consumer started for topic {Topic} (group {Group})",
            _topic, _groupId);

        try
        {
            await foreach (var (key, body) in _bus.SubscribeAsync(_topic, ct))
            {
                try
                {
                    var value = JsonSerializer.Deserialize<T>(body);
                    if (value != null)
                        await ProcessAsync(key, value, ct);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Failed to deserialize message from {Topic}", _topic);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing message from {Topic}", _topic);
                    // No commit semantics — message is already consumed. Loss
                    // here is the same tradeoff documented in
                    // InProcessMessageBus (acceptable at hobby scale; SDK-side
                    // retry covers transient loss).
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }
}
