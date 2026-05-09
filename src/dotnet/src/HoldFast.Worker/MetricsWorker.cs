using HoldFast.Analytics;
using HoldFast.Analytics.Models;
using HoldFast.Shared.Kafka;
using HoldFast.Shared.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HoldFast.Worker;

/// <summary>
/// Kafka message for OTeL-ingested metric data points. Shape matches the
/// OTeL spec's NumberDataPoint / HistogramDataPoint envelopes — the
/// metrics_sum / metrics_histogram tables are populated directly from
/// these fields.
/// </summary>
public record MetricsMessage(
    int ProjectId,
    string ServiceName,
    string MetricName,
    string MetricDescription,
    string MetricUnit,
    MetricKind Kind,
    DateTime StartTimestamp,
    DateTime Timestamp,
    Dictionary<string, string>? Attributes,
    string SecureSessionId,
    // Sum / Gauge
    double Value,
    int AggregationTemporality,
    bool IsMonotonic,
    // Histogram
    ulong Count,
    double Sum,
    List<ulong>? BucketCounts,
    List<double>? ExplicitBounds,
    double Min,
    double Max)
{
    /// <summary>
    /// Build a Gauge-shaped message from the legacy SDK-push narrow form
    /// (sessionId, name, value, category, ts, tags). Used by tests and by
    /// any caller that still has the old positional shape on hand. The
    /// category survives the round-trip so existing assertions keep working.
    /// </summary>
    public static MetricsMessage ForGauge(
        string sessionSecureId,
        string name,
        double value,
        string? category,
        DateTime timestamp,
        Dictionary<string, string>? tags) =>
        new(
            ProjectId: 0,
            ServiceName: string.Empty,
            MetricName: name,
            MetricDescription: category ?? string.Empty,
            MetricUnit: string.Empty,
            Kind: MetricKind.Gauge,
            StartTimestamp: timestamp,
            Timestamp: timestamp,
            Attributes: tags,
            SecureSessionId: sessionSecureId,
            Value: value,
            AggregationTemporality: 0,
            IsMonotonic: false,
            Count: 0UL,
            Sum: 0.0,
            BucketCounts: null,
            ExplicitBounds: null,
            Min: 0.0,
            Max: 0.0);
}

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
        _logger.LogDebug("Processing metric: {Name} ({Kind}) = {Value}",
            value.MetricName, value.Kind, value.Value);

        using var scope = _scopeFactory.CreateScope();
        var metricStore = scope.ServiceProvider.GetRequiredService<IMetricStore>();

        var row = new MetricRowInput
        {
            ProjectId = value.ProjectId,
            ServiceName = value.ServiceName,
            MetricName = value.MetricName,
            MetricDescription = value.MetricDescription,
            MetricUnit = value.MetricUnit,
            Kind = value.Kind,
            StartTimestamp = value.StartTimestamp,
            Timestamp = value.Timestamp,
            Attributes = value.Attributes ?? new Dictionary<string, string>(),
            SecureSessionId = value.SecureSessionId,
            Value = value.Value,
            AggregationTemporality = value.AggregationTemporality,
            IsMonotonic = value.IsMonotonic,
            Count = value.Count,
            Sum = value.Sum,
            BucketCounts = value.BucketCounts ?? new List<ulong>(),
            ExplicitBounds = value.ExplicitBounds ?? new List<double>(),
            Min = value.Min,
            Max = value.Max,
        };

        await metricStore.WriteMetricAsync(row, ct);
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
