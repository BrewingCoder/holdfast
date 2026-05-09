using HoldFast.Analytics.Models;

namespace HoldFast.Analytics;

/// <summary>
/// Backend-neutral store for application log analytics.
/// One implementation per analytics backend (currently ClickHouse only;
/// HOL-26+ will add Postgres).
/// </summary>
public interface ILogStore
{
    Task<LogConnection> ReadLogsAsync(
        int projectId,
        QueryInput query,
        ClickHousePagination pagination,
        CancellationToken ct = default);

    Task<List<HistogramBucket>> ReadLogsHistogramAsync(
        int projectId,
        QueryInput query,
        CancellationToken ct = default);

    Task<List<string>> GetLogKeysAsync(
        int projectId,
        QueryInput query,
        CancellationToken ct = default);

    Task<List<string>> GetLogKeyValuesAsync(
        int projectId,
        string key,
        QueryInput query,
        CancellationToken ct = default);

    Task<long> CountLogsAsync(
        int projectId,
        string? query,
        DateTime startDate,
        DateTime endDate,
        CancellationToken ct = default);

    Task WriteLogsAsync(
        IEnumerable<LogRowInput> logs,
        CancellationToken ct = default);
}
