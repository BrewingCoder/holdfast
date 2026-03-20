using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HoldFast.Shared.Kafka;

/// <summary>
/// Base Kafka consumer that handles deserialization and error handling.
/// Worker BackgroundServices inherit from this.
/// </summary>
public abstract class KafkaConsumerService<T> where T : class
{
    private readonly IConsumer<string, string> _consumer;
    private readonly ILogger _logger;
    private readonly string _topic;

    protected KafkaConsumerService(
        IOptions<KafkaOptions> options,
        string topic,
        string groupId,
        ILogger logger)
    {
        _topic = topic;
        _logger = logger;

        var config = new ConsumerConfig
        {
            BootstrapServers = options.Value.BootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            MaxPollIntervalMs = 300000,
        };
        _consumer = new ConsumerBuilder<string, string>(config).Build();
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
        _consumer.Subscribe(_topic);
        _logger.LogInformation("Kafka consumer started for topic {Topic}", _topic);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = _consumer.Consume(ct);
                if (result?.Message?.Value == null) continue;

                try
                {
                    var value = JsonSerializer.Deserialize<T>(result.Message.Value);
                    if (value != null)
                    {
                        await ProcessAsync(result.Message.Key, value, ct);
                    }
                    _consumer.Commit(result);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Failed to deserialize message from {Topic}", _topic);
                    _consumer.Commit(result); // Skip bad messages
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing message from {Topic}", _topic);
                    // Don't commit — message will be redelivered
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        finally
        {
            _consumer.Close();
        }
    }
}
