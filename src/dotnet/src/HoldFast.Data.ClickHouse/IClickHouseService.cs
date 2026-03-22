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

    Task<List<HistogramBucket>> ReadSessionsHistogramAsync(
        int projectId,
        QueryInput query,
        CancellationToken ct = default);

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

    Task<List<QueryKey>> GetErrorsKeysAsync(
        int projectId,
        DateTime startDate,
        DateTime endDate,
        string? query,
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

    // ── Alert State ───────────────────────────────────────────────

    Task<long> CountLogsAsync(
        int projectId,
        string? query,
        DateTime startDate,
        DateTime endDate,
        CancellationToken ct = default);

    Task<List<AlertStateChangeRow>> GetLastAlertStateChangesAsync(
        int projectId,
        int alertId,
        DateTime startDate,
        DateTime endDate,
        CancellationToken ct = default);

    Task<List<AlertStateChangeRow>> GetAlertingAlertStateChangesAsync(
        int projectId,
        int alertId,
        DateTime startDate,
        DateTime endDate,
        CancellationToken ct = default);

    Task<List<AlertStateChangeRow>> GetLastAlertingStatesAsync(
        int projectId,
        int alertId,
        DateTime startDate,
        DateTime endDate,
        CancellationToken ct = default);

    Task WriteAlertStateChangesAsync(
        int projectId,
        IEnumerable<AlertStateChangeRow> rows,
        CancellationToken ct = default);

    // ── Write Methods (Worker ingestion) ──────────────────────────

    /// <summary>
    /// Write a metric data point to ClickHouse.
    /// </summary>
    Task WriteMetricAsync(
        int projectId,
        string metricName,
        double metricValue,
        string? category,
        DateTime timestamp,
        Dictionary<string, string>? tags,
        string? sessionSecureId,
        CancellationToken ct = default);

    /// <summary>
    /// Write a batch of log rows to ClickHouse.
    /// </summary>
    Task WriteLogsAsync(
        IEnumerable<LogRowInput> logs,
        CancellationToken ct = default);

    /// <summary>
    /// Write a batch of trace span rows to ClickHouse.
    /// </summary>
    Task WriteTracesAsync(
        IEnumerable<TraceRowInput> traces,
        CancellationToken ct = default);

    /// <summary>
    /// Write a batch of session rows to ClickHouse for analytics queries.
    /// </summary>
    Task WriteSessionsAsync(
        IEnumerable<SessionRowInput> sessions,
        CancellationToken ct = default);

    /// <summary>
    /// Write a batch of error group rows to ClickHouse for analytics queries.
    /// </summary>
    Task WriteErrorGroupsAsync(
        IEnumerable<ErrorGroupRowInput> errorGroups,
        CancellationToken ct = default);

    /// <summary>
    /// Write a batch of error object rows to ClickHouse for analytics queries.
    /// </summary>
    Task WriteErrorObjectsAsync(
        IEnumerable<ErrorObjectRowInput> errorObjects,
        CancellationToken ct = default);
}
