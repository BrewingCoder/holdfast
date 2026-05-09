using HoldFast.Analytics.Models;

namespace HoldFast.Analytics;

/// <summary>
/// Backend-neutral store for metric time-series queries and writes.
/// </summary>
public interface IMetricStore
{
    Task<MetricsBuckets> ReadMetricsAsync(
        int projectId,
        QueryInput query,
        string bucketBy,
        List<string>? groupBy,
        string aggregator,
        string? column,
        CancellationToken ct = default);

    /// <summary>
    /// Writes a metric data point. Stores choose the destination table from
    /// <see cref="MetricRowInput.Kind"/>: Sum/Gauge → metrics_sum,
    /// Histogram → metrics_histogram, Summary → metrics_summary on the
    /// ClickHouse side. The Postgres store flattens to a single table.
    /// </summary>
    Task WriteMetricAsync(MetricRowInput row, CancellationToken ct = default);
}
