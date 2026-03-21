using HoldFast.Data.ClickHouse.Models;

namespace HoldFast.Data.ClickHouse;

/// <summary>
/// Interface for ClickHouse analytics queries.
/// Mirrors Go's clickhouse.Client with methods for logs, traces, sessions, and metrics.
/// </summary>
public interface IClickHouseService
{
    // ── Logs ─────────────────────────────────────────────────────────

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

    // ── Traces ───────────────────────────────────────────────────────

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

    // ── Sessions ─────────────────────────────────────────────────────

    Task<(List<int> Ids, long Total)> QuerySessionIdsAsync(
        int projectId,
        QueryInput query,
        int count,
        int page,
        string? sortField = null,
        bool sortDesc = true,
        CancellationToken ct = default);

    // ── Error Groups ─────────────────────────────────────────────────

    Task<(List<int> Ids, long Total)> QueryErrorGroupIdsAsync(
        int projectId,
        QueryInput query,
        int count,
        int page,
        CancellationToken ct = default);

    Task<List<HistogramBucket>> ReadErrorObjectsHistogramAsync(
        int projectId,
        QueryInput query,
        CancellationToken ct = default);

    // ── Metrics ──────────────────────────────────────────────────────

    Task<MetricsBuckets> ReadMetricsAsync(
        int projectId,
        QueryInput query,
        string bucketBy,
        List<string>? groupBy,
        string aggregator,
        string? column,
        CancellationToken ct = default);

    // ── Key Discovery (Sessions/Errors/Events) ─────────────────────

    Task<List<QueryKey>> GetSessionsKeysAsync(
        int projectId,
        DateTime startDate,
        DateTime endDate,
        string? query,
        CancellationToken ct = default);

    Task<List<string>> GetSessionsKeyValuesAsync(
        int projectId,
        string keyName,
        DateTime startDate,
        DateTime endDate,
        string? query,
        int? count,
        CancellationToken ct = default);

    Task<List<string>> GetErrorsKeyValuesAsync(
        int projectId,
        string keyName,
        DateTime startDate,
        DateTime endDate,
        string? query,
        int? count,
        CancellationToken ct = default);

    Task<List<QueryKey>> GetEventsKeysAsync(
        int projectId,
        DateTime startDate,
        DateTime endDate,
        string? query,
        string? eventName,
        CancellationToken ct = default);

    Task<List<string>> GetEventsKeyValuesAsync(
        int projectId,
        string keyName,
        DateTime startDate,
        DateTime endDate,
        string? query,
        int? count,
        string? eventName,
        CancellationToken ct = default);
}
