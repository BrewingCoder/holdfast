using System.Data;
using ClickHouse.Client.ADO;
using ClickHouse.Client.Utility;
using Dapper;
using HoldFast.Data.ClickHouse.Models;
using HoldFast.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HoldFast.Data.ClickHouse;

/// <summary>
/// Concrete ClickHouse analytics service using ClickHouse.Client + Dapper.
/// Mirrors Go's clickhouse.Client query methods.
/// </summary>
public class ClickHouseService : IClickHouseService, IDisposable
{
    private readonly ClickHouseConnection _conn;
    private readonly ClickHouseConnection _readonlyConn;
    private readonly ILogger<ClickHouseService> _logger;
    private readonly string _database;

    // Matching Go's LogsLimit default
    private const int DefaultLimit = 50;
    private const int MaxLimit = 10_000;
    private const int DefaultHistogramBuckets = 48;

    public ClickHouseService(IOptions<ClickHouseOptions> options, ILogger<ClickHouseService> logger)
    {
        _logger = logger;
        var opts = options.Value;
        _database = opts.Database;

        _conn = new ClickHouseConnection(opts.GetConnectionString(readOnly: false));
        _readonlyConn = new ClickHouseConnection(opts.GetConnectionString(readOnly: true));
    }

    // ── Logs ─────────────────────────────────────────────────────────

    public async Task<LogConnection> ReadLogsAsync(
        int projectId, QueryInput query, ClickHousePagination pagination, CancellationToken ct = default)
    {
        var limit = ClampLimit(pagination.Limit);
        var isDesc = pagination.Direction.Equals("DESC", StringComparison.OrdinalIgnoreCase);

        var sb = new SqlBuilder();
        sb.Append("SELECT Timestamp, ProjectId, TraceId, SpanId, SecureSessionId, UUID, ");
        sb.Append("TraceFlags, SeverityText, SeverityNumber, Source, ServiceName, ServiceVersion, ");
        sb.Append("Body, LogAttributes, Environment ");
        sb.Append("FROM logs ");
        sb.Append("WHERE ProjectId = {projectId:Int32} ");
        sb.AddParam("projectId", projectId);

        AppendDateRange(sb, query);
        AppendQueryFilter(sb, query.Query, "LogAttributes");
        AppendCursorCondition(sb, pagination, isDesc);

        sb.Append($"ORDER BY Timestamp {(isDesc ? "DESC" : "ASC")}, UUID {(isDesc ? "DESC" : "ASC")} ");
        sb.Append($"LIMIT {limit + 1} ");

        var rows = (await QueryAsync<LogRow>(sb, ct)).ToList();

        return BuildConnection(rows, limit, pagination, r => new LogEdge { Node = r, Cursor = r.Cursor });
    }

    public async Task<List<HistogramBucket>> ReadLogsHistogramAsync(
        int projectId, QueryInput query, CancellationToken ct = default)
    {
        return await ReadHistogramAsync("logs", "SeverityText", projectId, query, ct);
    }

    public async Task<List<string>> GetLogKeysAsync(
        int projectId, QueryInput query, CancellationToken ct = default)
    {
        var sb = new SqlBuilder();
        sb.Append("SELECT DISTINCT Key FROM log_keys ");
        sb.Append("WHERE ProjectId = {projectId:Int32} ");
        sb.AddParam("projectId", projectId);
        AppendKeyDateRange(sb, query);
        sb.Append("ORDER BY Key ");
        sb.Append("LIMIT 1000 ");

        return (await QueryAsync<string>(sb, ct, scalar: true)).ToList();
    }

    public async Task<List<string>> GetLogKeyValuesAsync(
        int projectId, string key, QueryInput query, CancellationToken ct = default)
    {
        var sb = new SqlBuilder();
        sb.Append("SELECT DISTINCT Value FROM log_key_values ");
        sb.Append("WHERE ProjectId = {projectId:Int32} AND Key = {key:String} ");
        sb.AddParam("projectId", projectId);
        sb.AddParam("key", key);
        AppendKeyDateRange(sb, query);
        sb.Append("ORDER BY Value ");
        sb.Append("LIMIT 500 ");

        return (await QueryAsync<string>(sb, ct, scalar: true)).ToList();
    }

    // ── Traces ───────────────────────────────────────────────────────

    public async Task<TraceConnection> ReadTracesAsync(
        int projectId, QueryInput query, ClickHousePagination pagination,
        bool omitBody = false, CancellationToken ct = default)
    {
        var limit = ClampLimit(pagination.Limit);
        var isDesc = pagination.Direction.Equals("DESC", StringComparison.OrdinalIgnoreCase);

        var sb = new SqlBuilder();
        sb.Append("SELECT Timestamp, UUID, TraceId, SpanId, ParentSpanId, TraceState, ");
        sb.Append("SpanName, SpanKind, ServiceName, ServiceVersion, TraceAttributes, Duration, ");
        sb.Append("StatusCode, StatusMessage, ProjectId, SecureSessionId, Environment, HasErrors, ");
        sb.Append("Events, Links ");
        sb.Append("FROM traces ");
        sb.Append("WHERE ProjectId = {projectId:Int32} ");
        sb.AddParam("projectId", projectId);

        AppendDateRange(sb, query);
        AppendQueryFilter(sb, query.Query, "TraceAttributes");
        AppendCursorCondition(sb, pagination, isDesc);

        sb.Append($"ORDER BY Timestamp {(isDesc ? "DESC" : "ASC")}, UUID {(isDesc ? "DESC" : "ASC")} ");
        sb.Append($"LIMIT {limit + 1} ");

        var rows = (await QueryAsync<TraceRow>(sb, ct)).ToList();

        return BuildConnection(rows, limit, pagination, r => new TraceEdge { Node = r, Cursor = r.Cursor });
    }

    public async Task<List<HistogramBucket>> ReadTracesHistogramAsync(
        int projectId, QueryInput query, CancellationToken ct = default)
    {
        return await ReadHistogramAsync("traces", "SpanName", projectId, query, ct);
    }

    public async Task<List<string>> GetTraceKeysAsync(
        int projectId, QueryInput query, CancellationToken ct = default)
    {
        var sb = new SqlBuilder();
        sb.Append("SELECT DISTINCT Key FROM trace_keys ");
        sb.Append("WHERE ProjectId = {projectId:Int32} ");
        sb.AddParam("projectId", projectId);
        AppendKeyDateRange(sb, query);
        sb.Append("ORDER BY Key ");
        sb.Append("LIMIT 1000 ");

        return (await QueryAsync<string>(sb, ct, scalar: true)).ToList();
    }

    public async Task<List<string>> GetTraceKeyValuesAsync(
        int projectId, string key, QueryInput query, CancellationToken ct = default)
    {
        var sb = new SqlBuilder();
        sb.Append("SELECT DISTINCT Value FROM trace_key_values ");
        sb.Append("WHERE ProjectId = {projectId:Int32} AND Key = {key:String} ");
        sb.AddParam("projectId", projectId);
        sb.AddParam("key", key);
        AppendKeyDateRange(sb, query);
        sb.Append("ORDER BY Value ");
        sb.Append("LIMIT 500 ");

        return (await QueryAsync<string>(sb, ct, scalar: true)).ToList();
    }

    // ── Sessions ─────────────────────────────────────────────────────

    public async Task<List<HistogramBucket>> ReadSessionsHistogramAsync(
        int projectId, QueryInput query, CancellationToken ct = default)
    {
        var sb = new SqlBuilder();
        sb.Append("SELECT toStartOfInterval(CreatedAt, INTERVAL 1 hour) AS BucketStart, ");
        sb.Append("toStartOfInterval(CreatedAt, INTERVAL 1 hour) + INTERVAL 1 hour AS BucketEnd, ");
        sb.Append("count(DISTINCT SecureSessionId) AS Count ");
        sb.Append("FROM sessions ");
        sb.Append($"WHERE ProjectId = {projectId} ");
        sb.Append($"AND CreatedAt >= '{query.DateRangeStart:yyyy-MM-dd HH:mm:ss}' ");
        sb.Append($"AND CreatedAt <= '{query.DateRangeEnd:yyyy-MM-dd HH:mm:ss}' ");
        sb.Append("GROUP BY BucketStart, BucketEnd ORDER BY BucketStart");

        return (await QueryAsync<HistogramBucket>(sb, ct)).ToList();
    }

    public async Task<(List<int> Ids, long Total)> QuerySessionIdsAsync(
        int projectId, QueryInput query, int count, int page,
        string? sortField = null, bool sortDesc = true, CancellationToken ct = default)
    {
        // Count query
        var countSb = new SqlBuilder();
        countSb.Append("SELECT count(DISTINCT SecureSessionId) FROM sessions ");
        countSb.Append("WHERE ProjectId = {projectId:Int32} ");
        countSb.AddParam("projectId", projectId);
        AppendDateRange(countSb, query);
        AppendQueryFilter(countSb, query.Query, "Fields");

        var total = await QueryScalarAsync<long>(countSb, ct);

        // IDs query
        var sb = new SqlBuilder();
        sb.Append("SELECT DISTINCT SecureSessionId FROM sessions ");
        sb.Append("WHERE ProjectId = {projectId:Int32} ");
        sb.AddParam("projectId", projectId);
        AppendDateRange(sb, query);
        AppendQueryFilter(sb, query.Query, "Fields");

        var sort = !string.IsNullOrEmpty(sortField) ? sortField : "Timestamp";
        sb.Append($"ORDER BY {sort} {(sortDesc ? "DESC" : "ASC")} ");
        sb.Append($"LIMIT {Math.Min(count, MaxLimit)} OFFSET {page * count} ");

        // Note: ClickHouse returns session IDs as strings; the resolver maps them to int IDs via Postgres
        var sessionIds = (await QueryAsync<int>(sb, ct, scalar: true)).ToList();

        return (sessionIds, total);
    }

    // ── Error Groups ─────────────────────────────────────────────────

    public async Task<(List<int> Ids, long Total)> QueryErrorGroupIdsAsync(
        int projectId, QueryInput query, int count, int page, CancellationToken ct = default)
    {
        // Count
        var countSb = new SqlBuilder();
        countSb.Append("SELECT count(DISTINCT ErrorGroupID) FROM error_objects ");
        countSb.Append("WHERE ProjectID = {projectId:Int32} ");
        countSb.AddParam("projectId", projectId);
        AppendDateRange(countSb, query);

        var total = await QueryScalarAsync<long>(countSb, ct);

        // IDs
        var sb = new SqlBuilder();
        sb.Append("SELECT ErrorGroupID, count(*) as cnt FROM error_objects ");
        sb.Append("WHERE ProjectID = {projectId:Int32} ");
        sb.AddParam("projectId", projectId);
        AppendDateRange(sb, query);
        sb.Append("GROUP BY ErrorGroupID ");
        sb.Append("ORDER BY cnt DESC ");
        sb.Append($"LIMIT {Math.Min(count, MaxLimit)} OFFSET {page * count} ");

        var ids = (await QueryAsync<int>(sb, ct, scalar: true)).ToList();

        return (ids, total);
    }

    public async Task<List<HistogramBucket>> ReadErrorObjectsHistogramAsync(
        int projectId, QueryInput query, CancellationToken ct = default)
    {
        return await ReadHistogramAsync("error_objects", null, projectId, query, ct);
    }

    // ── Metrics ──────────────────────────────────────────────────────

    public async Task<MetricsBuckets> ReadMetricsAsync(
        int projectId, QueryInput query, string bucketBy,
        List<string>? groupBy, string aggregator, string? column, CancellationToken ct = default)
    {
        var aggExpr = BuildAggregatorExpression(aggregator, column ?? "Value");
        var nBuckets = DefaultHistogramBuckets;

        var sb = new SqlBuilder();

        if (bucketBy == "none")
        {
            // No time bucketing — single result
            var groupByClause = groupBy?.Count > 0 ? string.Join(", ", groupBy) : null;

            sb.Append($"SELECT {aggExpr} as Value, count(*) as Count ");
            if (groupByClause != null)
                sb.Append($", {groupByClause} as Group ");
            sb.Append("FROM metrics ");
            sb.Append("WHERE ProjectId = {projectId:Int32} ");
            sb.AddParam("projectId", projectId);
            AppendDateRange(sb, query);
            AppendQueryFilter(sb, query.Query, "Attributes");
            if (groupByClause != null)
                sb.Append($"GROUP BY {groupByClause} ");
            sb.Append("ORDER BY Value DESC ");
            sb.Append("LIMIT 100 ");
        }
        else
        {
            // Time bucketing
            sb.Append("SELECT ");
            sb.Append($"  toStartOfInterval(Timestamp, toIntervalSecond(intDiv(toUnixTimestamp({{endDate:DateTime}}) - toUnixTimestamp({{startDate:DateTime}}), {nBuckets}))) as BucketStart, ");
            sb.Append($"  {aggExpr} as Value, ");
            sb.Append("  count(*) as Count ");
            var groupByClause = groupBy?.Count > 0 ? string.Join(", ", groupBy) : null;
            if (groupByClause != null)
                sb.Append($", {groupByClause} as `Group` ");
            sb.Append("FROM metrics ");
            sb.Append("WHERE ProjectId = {projectId:Int32} ");
            sb.AddParam("projectId", projectId);
            sb.AddParam("startDate", query.DateRangeStart);
            sb.AddParam("endDate", query.DateRangeEnd);
            AppendDateRange(sb, query);
            AppendQueryFilter(sb, query.Query, "Attributes");
            sb.Append("GROUP BY BucketStart ");
            if (groupByClause != null)
                sb.Append($", {groupByClause} ");
            sb.Append("ORDER BY BucketStart ASC ");
        }

        var buckets = (await QueryAsync<MetricsBucket>(sb, ct)).ToList();
        var totalCount = buckets.Sum(b => b.Count);

        return new MetricsBuckets
        {
            Buckets = buckets,
            TotalCount = totalCount,
        };
    }

    // ── Health ────────────────────────────────────────────────────────

    public async Task<bool> HealthCheckAsync(CancellationToken ct = default)
    {
        try
        {
            await _readonlyConn.ExecuteScalarAsync("SELECT 1", ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ClickHouse health check failed");
            return false;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private async Task<List<HistogramBucket>> ReadHistogramAsync(
        string table, string? groupColumn, int projectId, QueryInput query, CancellationToken ct)
    {
        var nBuckets = DefaultHistogramBuckets;

        var sb = new SqlBuilder();
        sb.Append("SELECT ");
        sb.Append($"  toStartOfInterval(Timestamp, toIntervalSecond(intDiv(toUnixTimestamp({{endDate:DateTime}}) - toUnixTimestamp({{startDate:DateTime}}), {nBuckets}))) as BucketStart, ");
        sb.Append("  count(*) as Count ");
        if (groupColumn != null)
            sb.Append($", {groupColumn} as `Group` ");
        sb.Append($"FROM {table} ");
        sb.Append("WHERE ProjectId = {projectId:Int32} ");
        sb.AddParam("projectId", projectId);
        sb.AddParam("startDate", query.DateRangeStart);
        sb.AddParam("endDate", query.DateRangeEnd);
        AppendDateRange(sb, query);
        sb.Append("GROUP BY BucketStart ");
        if (groupColumn != null)
            sb.Append($", {groupColumn} ");
        sb.Append("ORDER BY BucketStart ASC ");

        return (await QueryAsync<HistogramBucket>(sb, ct)).ToList();
    }

    private static string BuildAggregatorExpression(string aggregator, string column)
    {
        return aggregator.ToUpperInvariant() switch
        {
            "COUNT" => "count(*)",
            "COUNT_DISTINCT" or "COUNTDISTINCT" => $"uniq({column})",
            "SUM" => $"sum({column})",
            "AVG" => $"avg({column})",
            "MIN" => $"min({column})",
            "MAX" => $"max({column})",
            "P50" => $"quantile(0.50)({column})",
            "P90" => $"quantile(0.90)({column})",
            "P95" => $"quantile(0.95)({column})",
            "P99" => $"quantile(0.99)({column})",
            _ => "count(*)",
        };
    }

    private static void AppendDateRange(SqlBuilder sb, QueryInput query)
    {
        if (query.DateRangeStart != default)
        {
            sb.Append("AND Timestamp >= {startDate:DateTime} ");
            sb.AddParam("startDate", query.DateRangeStart);
        }

        if (query.DateRangeEnd != default)
        {
            sb.Append("AND Timestamp <= {endDate:DateTime} ");
            sb.AddParam("endDate", query.DateRangeEnd);
        }
    }

    private static void AppendKeyDateRange(SqlBuilder sb, QueryInput query)
    {
        if (query.DateRangeStart != default)
        {
            sb.Append("AND Day >= toDate({startDate:DateTime}) ");
            sb.AddParam("startDate", query.DateRangeStart);
        }

        if (query.DateRangeEnd != default)
        {
            sb.Append("AND Day <= toDate({endDate:DateTime}) ");
            sb.AddParam("endDate", query.DateRangeEnd);
        }
    }

    private static void AppendQueryFilter(SqlBuilder sb, string queryStr, string attributesColumn)
    {
        if (string.IsNullOrWhiteSpace(queryStr))
            return;

        // Simple body/attribute search — more complex query parsing (ANDs, ORs, key:value)
        // will be implemented when we port Go's parser/listener package
        sb.Append($"AND (Body ILIKE {{query:String}} OR {attributesColumn}['service_name'] ILIKE {{query:String}}) ");
        sb.AddParam("query", $"%{queryStr}%");
    }

    private static void AppendCursorCondition(SqlBuilder sb, ClickHousePagination pagination, bool isDesc)
    {
        string? cursorValue = pagination.After ?? pagination.Before ?? pagination.At;
        if (cursorValue == null || !CursorHelper.TryDecode(cursorValue, out var ts, out var uuid))
            return;

        var comp = isDesc ? "<" : ">";
        if (pagination.Before != null)
            comp = isDesc ? ">" : "<";

        sb.Append($"AND (Timestamp {comp} {{cursorTs:DateTime}} ");
        sb.Append($"  OR (Timestamp = {{cursorTs:DateTime}} AND UUID {comp} {{cursorUuid:String}})) ");
        sb.AddParam("cursorTs", ts);
        sb.AddParam("cursorUuid", uuid);
    }

    private LogConnection BuildConnection(
        List<LogRow> rows, int limit, ClickHousePagination pagination, Func<LogRow, LogEdge> toEdge)
    {
        var edges = rows.Select(toEdge).ToList();
        var pageInfo = ComputePageInfo(edges.Select(e => e.Cursor).ToList(), ref edges, limit, pagination);

        return new LogConnection
        {
            Edges = edges,
            PageInfo = pageInfo,
        };
    }

    private TraceConnection BuildConnection(
        List<TraceRow> rows, int limit, ClickHousePagination pagination, Func<TraceRow, TraceEdge> toEdge)
    {
        var edges = rows.Select(toEdge).ToList();
        var pageInfo = ComputePageInfo(edges.Select(e => e.Cursor).ToList(), ref edges, limit, pagination);

        return new TraceConnection
        {
            Edges = edges,
            PageInfo = pageInfo,
        };
    }

    /// <summary>
    /// Compute page info and trim edges, matching Go's getConnection logic.
    /// </summary>
    internal static PageInfo ComputePageInfo<TEdge>(
        List<string> cursors, ref List<TEdge> edges, int limit, ClickHousePagination pagination)
    {
        bool hasNextPage = false, hasPreviousPage = false;

        if (!string.IsNullOrEmpty(pagination.At))
        {
            var idx = cursors.IndexOf(pagination.At);
            if (idx >= 0)
            {
                var beforeCount = idx;
                var afterCount = edges.Count - idx - 1;

                if (beforeCount == limit / 2 + 1)
                {
                    hasPreviousPage = true;
                    edges = edges.Skip(1).ToList();
                    cursors = cursors.Skip(1).ToList();
                }

                if (afterCount == limit / 2 + 1)
                {
                    hasNextPage = true;
                    edges = edges.Take(edges.Count - 1).ToList();
                    cursors = cursors.Take(cursors.Count - 1).ToList();
                }
            }
        }
        else if (!string.IsNullOrEmpty(pagination.After))
        {
            hasPreviousPage = true;
            if (edges.Count >= limit + 1)
            {
                hasNextPage = true;
                edges = edges.Take(limit).ToList();
                cursors = cursors.Take(limit).ToList();
            }
        }
        else if (!string.IsNullOrEmpty(pagination.Before))
        {
            hasNextPage = true;
            if (edges.Count >= limit + 1)
            {
                hasPreviousPage = true;
                // Go: edges[1 : len(edges)-1] — remove first AND last
                edges = edges.Skip(1).Take(edges.Count - 2).ToList();
                cursors = cursors.Skip(1).Take(cursors.Count - 2).ToList();
            }
        }
        else
        {
            if (edges.Count >= limit + 1)
            {
                hasNextPage = true;
                edges = edges.Take(limit).ToList();
                cursors = cursors.Take(limit).ToList();
            }
        }

        return new PageInfo
        {
            HasNextPage = hasNextPage,
            HasPreviousPage = hasPreviousPage,
            StartCursor = cursors.Count > 0 ? cursors[0] : null,
            EndCursor = cursors.Count > 0 ? cursors[^1] : null,
        };
    }

    private static int ClampLimit(int limit)
    {
        return Math.Clamp(limit, 1, MaxLimit);
    }

    private async Task<IEnumerable<T>> QueryAsync<T>(SqlBuilder sb, CancellationToken ct, bool scalar = false)
    {
        var (sql, parameters) = sb.Build();
        _logger.LogDebug("ClickHouse query: {Sql}", sql);

        try
        {
            await EnsureOpenAsync(_readonlyConn, ct);
            if (scalar)
            {
                using var reader = await _readonlyConn.ExecuteReaderAsync(sql, ct);
                var results = new List<T>();
                while (await reader.ReadAsync(ct))
                {
                    results.Add((T)reader.GetValue(0));
                }
                return results;
            }

            return await _readonlyConn.QueryAsync<T>(sql);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClickHouse query failed: {Sql}", sql);
            throw;
        }
    }

    private async Task<T> QueryScalarAsync<T>(SqlBuilder sb, CancellationToken ct)
    {
        var (sql, _) = sb.Build();
        _logger.LogDebug("ClickHouse scalar query: {Sql}", sql);

        try
        {
            await EnsureOpenAsync(_readonlyConn, ct);
            var result = await _readonlyConn.ExecuteScalarAsync(sql, ct);
            return (T)Convert.ChangeType(result!, typeof(T));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClickHouse scalar query failed: {Sql}", sql);
            throw;
        }
    }

    private static async Task EnsureOpenAsync(ClickHouseConnection conn, CancellationToken ct)
    {
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);
    }

    // ── Key Discovery (Sessions/Errors/Events) ─────────────────────

    public async Task<List<QueryKey>> GetSessionsKeysAsync(
        int projectId, DateTime startDate, DateTime endDate, string? query, CancellationToken ct)
    {
        // Sessions keys are a hybrid: reserved column keys + dynamic field keys from ClickHouse
        var reservedKeys = new[] {
            "identifier", "city", "state", "country", "os_name", "os_version",
            "browser_name", "browser_version", "environment", "device_id",
            "fingerprint", "has_errors", "has_rage_clicks", "pages_visited",
            "active_length", "length", "processed", "first_time", "viewed"
        };

        var keys = reservedKeys
            .Where(k => string.IsNullOrEmpty(query) || k.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(k => new QueryKey { Name = k, Type = "String" })
            .ToList();

        return await Task.FromResult(keys);
    }

    public async Task<List<string>> GetSessionsKeyValuesAsync(
        int projectId, string keyName, DateTime startDate, DateTime endDate,
        string? query, int? count, CancellationToken ct)
    {
        var limit = count ?? 10;
        var sql =
            "SELECT DISTINCT Value FROM fields " +
            "WHERE ProjectID = {projectId:Int32} " +
            "AND Name = {keyName:String} " +
            "AND SessionCreatedAt >= {start:DateTime} " +
            "AND SessionCreatedAt <= {end:DateTime} " +
            "LIMIT {limit:Int32}";

        await EnsureOpenAsync(_readonlyConn, ct);
        var rows = await _readonlyConn.QueryAsync<string>(sql);
        return rows.ToList();
    }

    public async Task<List<QueryKey>> GetErrorsKeysAsync(
        int projectId, DateTime startDate, DateTime endDate, string? query, CancellationToken ct)
    {
        var reservedKeys = new[] {
            "event", "type", "url", "source", "stackTrace", "timestamp",
            "os", "browser", "environment", "service_name", "service_version"
        };

        var keys = reservedKeys
            .Where(k => string.IsNullOrEmpty(query) || k.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(k => new QueryKey { Name = k, Type = "String" })
            .ToList();

        return await Task.FromResult(keys);
    }

    public async Task<List<string>> GetErrorsKeyValuesAsync(
        int projectId, string keyName, DateTime startDate, DateTime endDate,
        string? query, int? count, CancellationToken ct)
    {
        var limit = count ?? 10;
        var col = SanitizeColumnName(keyName);
        var sql =
            $"SELECT DISTINCT {col} FROM error_groups " +
            "WHERE ProjectID = {projectId:Int32} " +
            "AND CreatedAt >= {start:DateTime} " +
            "AND CreatedAt <= {end:DateTime} " +
            "LIMIT {limit:Int32}";

        await EnsureOpenAsync(_readonlyConn, ct);
        var rows = await _readonlyConn.QueryAsync<string>(sql);
        return rows.Where(v => !string.IsNullOrEmpty(v)).ToList();
    }

    public async Task<List<QueryKey>> GetEventsKeysAsync(
        int projectId, DateTime startDate, DateTime endDate,
        string? query, string? eventName, CancellationToken ct)
    {
        // Events keys are primarily custom event attributes
        var reservedKeys = new[] { "event", "timestamp", "session_id" };

        var keys = reservedKeys
            .Where(k => string.IsNullOrEmpty(query) || k.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(k => new QueryKey { Name = k, Type = "String" })
            .ToList();

        return await Task.FromResult(keys);
    }

    public async Task<List<string>> GetEventsKeyValuesAsync(
        int projectId, string keyName, DateTime startDate, DateTime endDate,
        string? query, int? count, string? eventName, CancellationToken ct)
    {
        var limit = count ?? 10;
        var sql =
            "SELECT DISTINCT Value FROM fields " +
            "WHERE ProjectID = {projectId:Int32} " +
            "AND Name = {keyName:String} " +
            "AND SessionCreatedAt >= {start:DateTime} " +
            "AND SessionCreatedAt <= {end:DateTime} " +
            "LIMIT {limit:Int32}";

        await EnsureOpenAsync(_readonlyConn, ct);
        var rows = await _readonlyConn.QueryAsync<string>(sql);
        return rows.ToList();
    }

    /// <summary>
    /// Sanitize a column name for safe use in SQL (prevents injection).
    /// </summary>
    private static string SanitizeColumnName(string name)
    {
        // Only allow alphanumeric and underscore
        return new string(name.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
    }

    // ── Write Methods (Worker ingestion) ──────────────────────────

    public async Task WriteMetricAsync(
        int projectId,
        string metricName,
        double metricValue,
        string? category,
        DateTime timestamp,
        Dictionary<string, string>? tags,
        string? sessionSecureId,
        CancellationToken ct)
    {
        var sql =
            "INSERT INTO metrics_sum (ProjectId, MetricName, MetricValue, " +
            "Category, Timestamp, Tags.Name, Tags.Value, SecureSessionId) " +
            "VALUES ({projectId:Int32}, {metricName:String}, {metricValue:Float64}, " +
            "{category:String}, {timestamp:DateTime64(9)}, {tagNames:Array(String)}, " +
            "{tagValues:Array(String)}, {sessionSecureId:String})";

        var tagNames = tags?.Keys.ToArray() ?? [];
        var tagValues = tags?.Values.ToArray() ?? [];

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.AddParameter("projectId", projectId);
        cmd.AddParameter("metricName", metricName);
        cmd.AddParameter("metricValue", metricValue);
        cmd.AddParameter("category", category ?? "");
        cmd.AddParameter("timestamp", timestamp);
        cmd.AddParameter("tagNames", tagNames);
        cmd.AddParameter("tagValues", tagValues);
        cmd.AddParameter("sessionSecureId", sessionSecureId ?? "");

        await EnsureOpenAsync(_conn, ct);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task WriteLogsAsync(IEnumerable<LogRowInput> logs, CancellationToken ct)
    {
        var logList = logs.ToList();
        if (logList.Count == 0) return;

        await EnsureOpenAsync(_conn, ct);

        foreach (var l in logList)
        {
            var sql =
                "INSERT INTO logs (ProjectId, Timestamp, TraceId, SpanId, " +
                "SecureSessionId, SeverityText, SeverityNumber, Source, " +
                "ServiceName, ServiceVersion, Body, Environment) " +
                "VALUES ({projectId:Int32}, {ts:DateTime64(9)}, {traceId:String}, {spanId:String}, " +
                "{sessionId:String}, {severity:String}, {severityNum:Int32}, {source:String}, " +
                "{svcName:String}, {svcVer:String}, {body:String}, {env:String})";

            using var cmd = _conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.AddParameter("projectId", l.ProjectId);
            cmd.AddParameter("ts", l.Timestamp);
            cmd.AddParameter("traceId", l.TraceId);
            cmd.AddParameter("spanId", l.SpanId);
            cmd.AddParameter("sessionId", l.SecureSessionId);
            cmd.AddParameter("severity", l.SeverityText);
            cmd.AddParameter("severityNum", l.SeverityNumber);
            cmd.AddParameter("source", l.Source);
            cmd.AddParameter("svcName", l.ServiceName);
            cmd.AddParameter("svcVer", l.ServiceVersion);
            cmd.AddParameter("body", l.Body);
            cmd.AddParameter("env", l.Environment);

            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task WriteTracesAsync(IEnumerable<TraceRowInput> traces, CancellationToken ct)
    {
        var traceList = traces.ToList();
        if (traceList.Count == 0) return;

        await EnsureOpenAsync(_conn, ct);

        foreach (var t in traceList)
        {
            var sql =
                "INSERT INTO traces (ProjectId, Timestamp, TraceId, SpanId, ParentSpanId, " +
                "SecureSessionId, ServiceName, ServiceVersion, Environment, " +
                "SpanName, SpanKind, Duration, StatusCode, StatusMessage, HasErrors) " +
                "VALUES ({projectId:Int32}, {ts:DateTime64(9)}, {traceId:String}, {spanId:String}, " +
                "{parentSpanId:String}, {sessionId:String}, {svcName:String}, {svcVer:String}, " +
                "{env:String}, {spanName:String}, {spanKind:String}, {duration:Int64}, " +
                "{statusCode:String}, {statusMsg:String}, {hasErrors:UInt8})";

            using var cmd = _conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.AddParameter("projectId", t.ProjectId);
            cmd.AddParameter("ts", t.Timestamp);
            cmd.AddParameter("traceId", t.TraceId);
            cmd.AddParameter("spanId", t.SpanId);
            cmd.AddParameter("parentSpanId", t.ParentSpanId);
            cmd.AddParameter("sessionId", t.SecureSessionId);
            cmd.AddParameter("svcName", t.ServiceName);
            cmd.AddParameter("svcVer", t.ServiceVersion);
            cmd.AddParameter("env", t.Environment);
            cmd.AddParameter("spanName", t.SpanName);
            cmd.AddParameter("spanKind", t.SpanKind);
            cmd.AddParameter("duration", t.Duration);
            cmd.AddParameter("statusCode", t.StatusCode);
            cmd.AddParameter("statusMsg", t.StatusMessage);
            cmd.AddParameter("hasErrors", t.HasErrors ? (byte)1 : (byte)0);

            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task WriteSessionsAsync(IEnumerable<SessionRowInput> sessions, CancellationToken ct)
    {
        var list = sessions.ToList();
        if (list.Count == 0) return;

        await EnsureOpenAsync(_conn, ct);

        foreach (var s in list)
        {
            var sql =
                "INSERT INTO sessions (ProjectId, SessionId, SecureSessionId, CreatedAt, " +
                "Identifier, OSName, OSVersion, BrowserName, BrowserVersion, " +
                "City, State, Country, Environment, AppVersion, ServiceName, " +
                "ActiveLength, Length, PagesVisited, HasErrors, HasRageClicks, " +
                "Processed, FirstTime) " +
                "VALUES ({projectId:Int32}, {sessionId:Int32}, {secureSessionId:String}, " +
                "{createdAt:DateTime64(9)}, {identifier:String}, {osName:String}, " +
                "{osVersion:String}, {browserName:String}, {browserVersion:String}, " +
                "{city:String}, {state:String}, {country:String}, {env:String}, " +
                "{appVersion:String}, {serviceName:String}, {activeLength:Int32}, " +
                "{length:Int32}, {pagesVisited:Int32}, {hasErrors:UInt8}, " +
                "{hasRageClicks:UInt8}, {processed:UInt8}, {firstTime:UInt8})";

            using var cmd = _conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.AddParameter("projectId", s.ProjectId);
            cmd.AddParameter("sessionId", s.SessionId);
            cmd.AddParameter("secureSessionId", s.SecureSessionId);
            cmd.AddParameter("createdAt", s.CreatedAt);
            cmd.AddParameter("identifier", s.Identifier ?? "");
            cmd.AddParameter("osName", s.OSName ?? "");
            cmd.AddParameter("osVersion", s.OSVersion ?? "");
            cmd.AddParameter("browserName", s.BrowserName ?? "");
            cmd.AddParameter("browserVersion", s.BrowserVersion ?? "");
            cmd.AddParameter("city", s.City ?? "");
            cmd.AddParameter("state", s.State ?? "");
            cmd.AddParameter("country", s.Country ?? "");
            cmd.AddParameter("env", s.Environment ?? "");
            cmd.AddParameter("appVersion", s.AppVersion ?? "");
            cmd.AddParameter("serviceName", s.ServiceName ?? "");
            cmd.AddParameter("activeLength", s.ActiveLength);
            cmd.AddParameter("length", s.Length);
            cmd.AddParameter("pagesVisited", s.PagesVisited);
            cmd.AddParameter("hasErrors", s.HasErrors ? (byte)1 : (byte)0);
            cmd.AddParameter("hasRageClicks", s.HasRageClicks ? (byte)1 : (byte)0);
            cmd.AddParameter("processed", s.Processed ? (byte)1 : (byte)0);
            cmd.AddParameter("firstTime", s.FirstTime ? (byte)1 : (byte)0);

            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task WriteErrorGroupsAsync(IEnumerable<ErrorGroupRowInput> errorGroups, CancellationToken ct)
    {
        var list = errorGroups.ToList();
        if (list.Count == 0) return;

        await EnsureOpenAsync(_conn, ct);

        foreach (var g in list)
        {
            var sql =
                "INSERT INTO error_groups (ProjectID, ErrorGroupID, SecureID, CreatedAt, " +
                "UpdatedAt, Event, Type, State, ServiceName, Environments) " +
                "VALUES ({projectId:Int32}, {errorGroupId:Int32}, {secureId:String}, " +
                "{createdAt:DateTime64(9)}, {updatedAt:DateTime64(9)}, {event:String}, " +
                "{type:String}, {state:String}, {serviceName:String}, {environments:String})";

            using var cmd = _conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.AddParameter("projectId", g.ProjectId);
            cmd.AddParameter("errorGroupId", g.ErrorGroupId);
            cmd.AddParameter("secureId", g.SecureId);
            cmd.AddParameter("createdAt", g.CreatedAt);
            cmd.AddParameter("updatedAt", g.UpdatedAt);
            cmd.AddParameter("event", g.Event ?? "");
            cmd.AddParameter("type", g.Type ?? "");
            cmd.AddParameter("state", g.State);
            cmd.AddParameter("serviceName", g.ServiceName ?? "");
            cmd.AddParameter("environments", g.Environments ?? "");

            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task WriteErrorObjectsAsync(IEnumerable<ErrorObjectRowInput> errorObjects, CancellationToken ct)
    {
        var list = errorObjects.ToList();
        if (list.Count == 0) return;

        await EnsureOpenAsync(_conn, ct);

        foreach (var e in list)
        {
            var sql =
                "INSERT INTO error_objects (ProjectID, ErrorObjectID, ErrorGroupID, " +
                "Timestamp, Event, Type, URL, Environment, OS, Browser, " +
                "ServiceName, ServiceVersion) " +
                "VALUES ({projectId:Int32}, {errorObjectId:Int32}, {errorGroupId:Int32}, " +
                "{ts:DateTime64(9)}, {event:String}, {type:String}, {url:String}, " +
                "{env:String}, {os:String}, {browser:String}, " +
                "{serviceName:String}, {serviceVersion:String})";

            using var cmd = _conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.AddParameter("projectId", e.ProjectId);
            cmd.AddParameter("errorObjectId", e.ErrorObjectId);
            cmd.AddParameter("errorGroupId", e.ErrorGroupId);
            cmd.AddParameter("ts", e.Timestamp);
            cmd.AddParameter("event", e.Event ?? "");
            cmd.AddParameter("type", e.Type ?? "");
            cmd.AddParameter("url", e.Url ?? "");
            cmd.AddParameter("env", e.Environment ?? "");
            cmd.AddParameter("os", e.OS ?? "");
            cmd.AddParameter("browser", e.Browser ?? "");
            cmd.AddParameter("serviceName", e.ServiceName ?? "");
            cmd.AddParameter("serviceVersion", e.ServiceVersion ?? "");

            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    // ── Alert State ───────────────────────────────────────────────────

    public Task<long> CountLogsAsync(
        int projectId, string? query, DateTime startDate, DateTime endDate,
        CancellationToken ct = default)
        => Task.FromResult(0L);

    public Task<List<AlertStateChangeRow>> GetLastAlertStateChangesAsync(
        int projectId, int alertId, DateTime startDate, DateTime endDate,
        CancellationToken ct = default)
        => Task.FromResult(new List<AlertStateChangeRow>());

    public Task<List<AlertStateChangeRow>> GetAlertingAlertStateChangesAsync(
        int projectId, int alertId, DateTime startDate, DateTime endDate,
        CancellationToken ct = default)
        => Task.FromResult(new List<AlertStateChangeRow>());

    public Task<List<AlertStateChangeRow>> GetLastAlertingStatesAsync(
        int projectId, int alertId, DateTime startDate, DateTime endDate,
        CancellationToken ct = default)
        => Task.FromResult(new List<AlertStateChangeRow>());

    public Task WriteAlertStateChangesAsync(
        int projectId, IEnumerable<AlertStateChangeRow> rows,
        CancellationToken ct = default)
        => Task.CompletedTask;

    public void Dispose()
    {
        _conn.Dispose();
        _readonlyConn.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Simple parameterized SQL builder for ClickHouse queries.
/// Uses ClickHouse parameterized query syntax: {name:Type}.
/// </summary>
public class SqlBuilder
{
    private readonly System.Text.StringBuilder _sql = new();
    private readonly Dictionary<string, object> _params = new();

    public void Append(string fragment)
    {
        _sql.Append(fragment);
    }

    public void AddParam(string name, object value)
    {
        _params.TryAdd(name, value);
    }

    public (string Sql, Dictionary<string, object> Parameters) Build()
    {
        return (_sql.ToString(), _params);
    }
}
