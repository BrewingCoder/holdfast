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

    Task WriteMetricAsync(
        int projectId,
        string metricName,
        double metricValue,
        string? category,
        DateTime timestamp,
        Dictionary<string, string>? tags,
        string? sessionSecureId,
        CancellationToken ct = default);
}
