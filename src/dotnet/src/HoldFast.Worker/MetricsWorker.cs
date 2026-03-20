using HoldFast.Shared.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HoldFast.Worker;

/// <summary>
/// Kafka message for custom metrics pushed by SDKs.
/// </summary>
public record MetricsMessage(
    string SessionSecureId,
    string Name,
    double Value,
    string? Category,
    DateTime Timestamp,
    Dictionary<string, string>? Tags);

/// <summary>
/// Consumes metrics from Kafka and writes to ClickHouse.
/// Replaces the Go worker's pushMetrics handler.
/// </summary>
public class MetricsConsumer : KafkaConsumerService<MetricsMessage>
{
    private readonly ILogger<MetricsConsumer> _logger;

    public MetricsConsumer(
        IOptions<KafkaOptions> options,
        ILogger<MetricsConsumer> logger)
        : base(options, KafkaTopics.Metrics, "metrics-worker", logger)
    {
        _logger = logger;
    }

    protected override async Task ProcessAsync(string key, MetricsMessage value, CancellationToken ct)
    {
        _logger.LogDebug("Processing metric: {Name} = {Value}", value.Name, value.Value);

        // TODO Phase 3: Write to ClickHouse via HoldFast.Data.ClickHouse
        await Task.CompletedTask;
    }
}

/// <summary>
/// BackgroundService that runs the metrics consumer.
/// </summary>
public class MetricsWorker : BackgroundService
{
    private readonly MetricsConsumer _consumer;

    public MetricsWorker(MetricsConsumer consumer)
    {
        _consumer = consumer;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return _consumer.ConsumeLoopAsync(stoppingToken);
    }
}
