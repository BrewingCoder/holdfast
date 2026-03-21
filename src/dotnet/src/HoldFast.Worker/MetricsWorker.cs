using HoldFast.Data.ClickHouse;
using HoldFast.Shared.Kafka;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MetricsConsumer> _logger;

    public MetricsConsumer(
        IOptions<KafkaOptions> options,
        IServiceScopeFactory scopeFactory,
        ILogger<MetricsConsumer> logger)
        : base(options, KafkaTopics.Metrics, "metrics-worker", logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ProcessAsync(string key, MetricsMessage value, CancellationToken ct)
    {
        _logger.LogDebug("Processing metric: {Name} = {Value}", value.Name, value.Value);

        using var scope = _scopeFactory.CreateScope();
        var clickHouse = scope.ServiceProvider.GetRequiredService<IClickHouseService>();

        // Resolve project ID from session (if available) — for now, use 0 as fallback
        // Full session lookup will be wired in Phase 3
        var projectId = 0;

        await clickHouse.WriteMetricAsync(
            projectId,
            value.Name,
            value.Value,
            value.Category,
            value.Timestamp,
            value.Tags,
            value.SessionSecureId,
            ct);
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
