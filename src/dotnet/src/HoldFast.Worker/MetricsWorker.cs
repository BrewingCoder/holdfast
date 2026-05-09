using HoldFast.Analytics;
using HoldFast.Shared.Kafka;
using HoldFast.Shared.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HoldFast.Worker;

/// <summary>
/// Kafka message for custom metrics pushed by SDKs.
///
/// Shape mirrors <see cref="HoldFast.GraphQL.Public.InputTypes.MetricInput"/>
/// so the in-process bus's JSON round-trip preserves the producer's payload.
/// Tags are an array of {Name, Value} objects on the wire — the consumer
/// flattens them to a Dictionary at the analytics-store boundary.
/// </summary>
public record MetricsMessage(
    string SessionSecureId,
    string? SpanId,
    string? ParentSpanId,
    string? TraceId,
    string? Group,
    string Name,
    double Value,
    string? Category,
    DateTime Timestamp,
    List<MetricTagPair>? Tags);

/// <summary>
/// On-the-wire shape of a MetricInput tag — matches MetricInput.Tags.
/// </summary>
public record MetricTagPair(string Name, string Value);

/// <summary>
/// Consumes metrics from Kafka and writes to ClickHouse.
/// Replaces the Go worker's pushMetrics handler.
/// </summary>
public class MetricsConsumer : MessageConsumerBase<MetricsMessage>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MetricsConsumer> _logger;

    public MetricsConsumer(
        IMessageBus bus,
        IServiceScopeFactory scopeFactory,
        ILogger<MetricsConsumer> logger)
        : base(bus, KafkaTopics.Metrics, "metrics-worker", logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ProcessAsync(string key, MetricsMessage value, CancellationToken ct)
    {
        _logger.LogDebug("Processing metric: {Name} = {Value}", value.Name, value.Value);

        using var scope = _scopeFactory.CreateScope();
        var metricStore = scope.ServiceProvider.GetRequiredService<IMetricStore>();

        // Resolve project ID from session (if available) — for now, use 0 as fallback
        // Full session lookup will be wired in Phase 3
        var projectId = 0;

        // MetricInput.Tags arrives as List<{Name, Value}> over the wire.
        // The IMetricStore expects Dictionary<string, string>; flatten here,
        // taking the last-wins value if a tag name repeats.
        Dictionary<string, string>? tagDict = null;
        if (value.Tags is { Count: > 0 })
        {
            tagDict = new Dictionary<string, string>();
            foreach (var tag in value.Tags)
                tagDict[tag.Name] = tag.Value;
        }

        await metricStore.WriteMetricAsync(
            projectId,
            value.Name,
            value.Value,
            value.Category,
            value.Timestamp,
            tagDict,
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
