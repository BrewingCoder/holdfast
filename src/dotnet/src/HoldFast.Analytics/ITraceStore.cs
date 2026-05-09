using HoldFast.Analytics.Models;

namespace HoldFast.Analytics;

/// <summary>
/// Backend-neutral store for distributed-trace span analytics.
/// </summary>
public interface ITraceStore
{
    Task<TraceConnection> ReadTracesAsync(
        int projectId,
        QueryInput query,
        ClickHousePagination pagination,
        bool omitBody = false,
        CancellationToken ct = default);

    Task<List<HistogramBucket>> ReadTracesHistogramAsync(
        int projectId,
        QueryInput query,
        CancellationToken ct = default);

    Task<List<string>> GetTraceKeysAsync(
        int projectId,
        QueryInput query,
        CancellationToken ct = default);

    Task<List<string>> GetTraceKeyValuesAsync(
        int projectId,
        string key,
        QueryInput query,
        CancellationToken ct = default);

    Task WriteTracesAsync(
        IEnumerable<TraceRowInput> traces,
        CancellationToken ct = default);
}
