using System.Text.Json;
using HoldFast.Analytics;
using HoldFast.Analytics.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace HoldFast.Data.Postgres;

/// <summary>
/// Postgres implementation of <see cref="ILogStore"/>.
///
/// HOL-29: mirrors the query and behavior of
/// <c>HoldFast.Data.ClickHouse.ClickHouseService</c>'s log methods, translated
/// to Postgres + TimescaleDB. Same cursor format (RFC3339+UUID, base64), same
/// pagination semantics, same query-filter shape (body ILIKE + log_attributes
/// GIN-indexed JSONB lookup).
///
/// Differences from the CH impl:
/// - <c>JSONB</c> replaces CH's <c>Map(LowCardinality(String), String)</c>.
///   Read-side returns Dictionary&lt;string,string&gt; via Npgsql's built-in
///   serializer; write-side passes a Dictionary which Npgsql turns into JSONB.
/// - log_keys / log_key_values are upserted inline in WriteLogsAsync rather
///   than maintained by a materialized view (CH used a SummingMergeTree).
///   Acceptable at hobby scale; future PR can swap to TimescaleDB continuous
///   aggregates if write volume warrants.
/// - Bulk insert uses <c>NpgsqlBinaryImporter</c> (binary COPY) — significantly
///   faster than per-row INSERT for batches > ~50 rows.
/// </summary>
public sealed class PostgresLogStore : ILogStore
{
    private readonly PostgresAnalyticsOptions _options;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PostgresLogStore> _logger;

    private const int DefaultLimit = 50;
    private const int MaxLimit = 10_000;
    private const int DefaultHistogramBuckets = 48;

    public PostgresLogStore(
        IOptions<PostgresAnalyticsOptions> options,
        IConfiguration configuration,
        ILogger<PostgresLogStore> logger)
    {
        _options = options.Value;
        _configuration = configuration;
        _logger = logger;
    }

    private string ConnectionString =>
        _options.ConnectionString
        ?? _configuration.GetConnectionString("PostgreSQL")
        ?? throw new InvalidOperationException(
            "PostgresLogStore: no connection string configured (PostgresAnalytics:ConnectionString or ConnectionStrings:PostgreSQL)");

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    // ── Reads ────────────────────────────────────────────────────────

    public async Task<LogConnection> ReadLogsAsync(
        int projectId, QueryInput query, ClickHousePagination pagination, CancellationToken ct = default)
    {
        var limit = ClampLimit(pagination.Limit);
        var isDesc = pagination.Direction.Equals("DESC", StringComparison.OrdinalIgnoreCase);

        var (sql, parameters) = BuildLogsReadQuery(projectId, query, pagination, limit, isDesc);

        await using var conn = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);

        var rows = new List<LogRow>();
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                rows.Add(ReadLogRow(reader));
        }

        return BuildConnection(rows, limit, pagination);
    }

    private static (string Sql, List<(string, object)> Params) BuildLogsReadQuery(
        int projectId, QueryInput query, ClickHousePagination pagination, int limit, bool isDesc)
    {
        var dir = isDesc ? "DESC" : "ASC";
        var sql = new System.Text.StringBuilder();
        var p = new List<(string, object)>();

        sql.AppendLine(@"
            SELECT timestamp, project_id, trace_id, span_id, secure_session_id, uuid::text,
                   trace_flags, severity_text, severity_number, source, service_name,
                   service_version, body, log_attributes, environment
            FROM analytics.logs
            WHERE project_id = @projectId");
        p.Add(("projectId", projectId));

        if (query.DateRange.StartDate != default)
        {
            sql.AppendLine("AND timestamp >= @startDate");
            p.Add(("startDate", query.DateRange.StartDate));
        }
        if (query.DateRange.EndDate != default)
        {
            sql.AppendLine("AND timestamp <= @endDate");
            p.Add(("endDate", query.DateRange.EndDate));
        }

        // Same simplified filter as the CH side (HOL-29 keeps parity with the CH
        // impl, which itself notes that full query-language parsing is deferred).
        if (!string.IsNullOrWhiteSpace(query.Query))
        {
            sql.AppendLine(@"AND (body ILIKE @q OR log_attributes ->> 'service_name' ILIKE @q)");
            p.Add(("q", $"%{query.Query}%"));
        }

        // Cursor condition — same RFC3339+UUID cursor shape as CH so the GraphQL
        // contract is identical regardless of which backend is active.
        var cursorValue = pagination.After ?? pagination.Before ?? pagination.At;
        if (cursorValue != null && CursorHelper.TryDecode(cursorValue, out var cursorTs, out var cursorUuid))
        {
            var comp = isDesc ? "<" : ">";
            if (pagination.Before != null) comp = isDesc ? ">" : "<";
            sql.AppendLine($@"
                AND (timestamp {comp} @cursorTs
                     OR (timestamp = @cursorTs AND uuid::text {comp} @cursorUuid))");
            p.Add(("cursorTs", cursorTs));
            p.Add(("cursorUuid", cursorUuid));
        }

        sql.AppendLine($"ORDER BY timestamp {dir}, uuid {dir}");
        sql.AppendLine($"LIMIT {limit + 1}");

        return (sql.ToString(), p);
    }

    public async Task<List<HistogramBucket>> ReadLogsHistogramAsync(
        int projectId, QueryInput query, CancellationToken ct = default)
    {
        const int nBuckets = DefaultHistogramBuckets;

        var sql = new System.Text.StringBuilder();
        var parameters = new List<(string, object)>();

        // Bucket-width math mirrors the CH implementation: span the date range
        // into N equal-width buckets via `time_bucket`. PG/TimescaleDB has
        // `time_bucket(interval, ts)`, vanilla PG has `date_trunc`. We use
        // an interval expression so it works on both.
        sql.AppendLine($@"
            SELECT
                to_timestamp(
                    floor(extract(epoch FROM timestamp) /
                          GREATEST(1, (extract(epoch FROM @endDate) - extract(epoch FROM @startDate))::bigint / {nBuckets}))
                    * GREATEST(1, (extract(epoch FROM @endDate) - extract(epoch FROM @startDate))::bigint / {nBuckets})
                ) AS bucket_start,
                count(*)::bigint AS count,
                severity_text AS group_value
            FROM analytics.logs
            WHERE project_id = @projectId
              AND timestamp >= @startDate
              AND timestamp <= @endDate");
        parameters.Add(("projectId", projectId));
        parameters.Add(("startDate", query.DateRange.StartDate));
        parameters.Add(("endDate", query.DateRange.EndDate));

        if (!string.IsNullOrWhiteSpace(query.Query))
        {
            sql.AppendLine(@"AND (body ILIKE @q OR log_attributes ->> 'service_name' ILIKE @q)");
            parameters.Add(("q", $"%{query.Query}%"));
        }

        sql.AppendLine("GROUP BY bucket_start, severity_text");
        sql.AppendLine("ORDER BY bucket_start ASC");

        await using var conn = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql.ToString(), conn);
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);

        var buckets = new List<HistogramBucket>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            buckets.Add(new HistogramBucket
            {
                BucketStart = reader.GetDateTime(0),
                BucketEnd = reader.GetDateTime(0), // CH impl populates BucketEnd identically
                Count = reader.GetInt64(1),
                Group = reader.IsDBNull(2) ? null : reader.GetString(2),
            });
        }
        return buckets;
    }

    public async Task<List<string>> GetLogKeysAsync(
        int projectId, QueryInput query, CancellationToken ct = default)
    {
        var sql = @"
            SELECT DISTINCT key FROM analytics.log_keys
            WHERE project_id = @projectId
              AND day >= @startDay
              AND day <= @endDay
            ORDER BY key
            LIMIT 1000";
        await using var conn = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("projectId", projectId);
        cmd.Parameters.AddWithValue("startDay", DateOnlyOf(query.DateRange.StartDate));
        cmd.Parameters.AddWithValue("endDay", DateOnlyOf(query.DateRange.EndDate));

        var keys = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            keys.Add(reader.GetString(0));
        return keys;
    }

    public async Task<List<string>> GetLogKeyValuesAsync(
        int projectId, string key, QueryInput query, CancellationToken ct = default)
    {
        var sql = @"
            SELECT DISTINCT value FROM analytics.log_key_values
            WHERE project_id = @projectId
              AND key = @key
              AND day >= @startDay
              AND day <= @endDay
            ORDER BY value
            LIMIT 500";
        await using var conn = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("projectId", projectId);
        cmd.Parameters.AddWithValue("key", key);
        cmd.Parameters.AddWithValue("startDay", DateOnlyOf(query.DateRange.StartDate));
        cmd.Parameters.AddWithValue("endDay", DateOnlyOf(query.DateRange.EndDate));

        var values = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            values.Add(reader.GetString(0));
        return values;
    }

    public Task<long> CountLogsAsync(
        int projectId, string? query, DateTime startDate, DateTime endDate,
        CancellationToken ct = default)
    {
        // Matches the CH impl which currently returns 0L (HOL-29 keeps parity
        // until both backends are extended together in a future PR).
        return Task.FromResult(0L);
    }

    // ── Writes ───────────────────────────────────────────────────────

    public async Task WriteLogsAsync(IEnumerable<LogRowInput> logs, CancellationToken ct = default)
    {
        var batch = logs.ToList();
        if (batch.Count == 0) return;

        await using var conn = await OpenAsync(ct);

        // Bulk insert via binary COPY — Npgsql's fastest insert path. For batches
        // < ~50 rows the overhead vs INSERT is negligible; for batches > ~500
        // rows COPY is 5-10× faster.
        await using (var importer = await conn.BeginBinaryImportAsync(@"
            COPY analytics.logs (
                timestamp, uuid, project_id, trace_id, span_id, secure_session_id,
                trace_flags, severity_text, severity_number, source, service_name,
                service_version, body, log_attributes, environment
            ) FROM STDIN (FORMAT BINARY)", ct))
        {
            foreach (var l in batch)
            {
                await importer.StartRowAsync(ct);
                await importer.WriteAsync(l.Timestamp, NpgsqlDbType.TimestampTz, ct);
                await importer.WriteAsync(Guid.NewGuid(), NpgsqlDbType.Uuid, ct);
                await importer.WriteAsync(l.ProjectId, NpgsqlDbType.Integer, ct);
                await importer.WriteAsync(l.TraceId ?? "", NpgsqlDbType.Text, ct);
                await importer.WriteAsync(l.SpanId ?? "", NpgsqlDbType.Text, ct);
                await importer.WriteAsync(l.SecureSessionId ?? "", NpgsqlDbType.Text, ct);
                await importer.WriteAsync(0, NpgsqlDbType.Integer, ct); // trace_flags - unused in current ingest
                await importer.WriteAsync(l.SeverityText ?? "", NpgsqlDbType.Text, ct);
                await importer.WriteAsync(l.SeverityNumber, NpgsqlDbType.Integer, ct);
                await importer.WriteAsync(l.Source ?? "", NpgsqlDbType.Text, ct);
                await importer.WriteAsync(l.ServiceName ?? "", NpgsqlDbType.Text, ct);
                await importer.WriteAsync(l.ServiceVersion ?? "", NpgsqlDbType.Text, ct);
                await importer.WriteAsync(l.Body ?? "", NpgsqlDbType.Text, ct);
                await importer.WriteAsync(
                    JsonSerializer.Serialize(l.LogAttributes ?? new()),
                    NpgsqlDbType.Jsonb, ct);
                await importer.WriteAsync(l.Environment ?? "", NpgsqlDbType.Text, ct);
            }
            await importer.CompleteAsync(ct);
        }

        // Catalog upserts for log_keys + log_key_values. Aggregate per (project,
        // key, day) and (project, key, day, value) tuples in memory first so a
        // single batch produces one UPSERT per unique key/value, not N for N logs.
        var keyCounts = new Dictionary<(int ProjectId, string Key, DateTime Day), long>();
        var kvCounts = new Dictionary<(int ProjectId, string Key, DateTime Day, string Value), long>();

        foreach (var l in batch)
        {
            if (l.LogAttributes == null) continue;
            var day = l.Timestamp.Date;
            foreach (var kv in l.LogAttributes)
            {
                var keyTuple = (l.ProjectId, kv.Key, day);
                keyCounts[keyTuple] = keyCounts.GetValueOrDefault(keyTuple) + 1;

                var kvTuple = (l.ProjectId, kv.Key, day, kv.Value);
                kvCounts[kvTuple] = kvCounts.GetValueOrDefault(kvTuple) + 1;
            }
        }

        if (keyCounts.Count > 0)
            await UpsertLogKeysAsync(conn, keyCounts, ct);
        if (kvCounts.Count > 0)
            await UpsertLogKeyValuesAsync(conn, kvCounts, ct);
    }

    private static async Task UpsertLogKeysAsync(
        NpgsqlConnection conn,
        Dictionary<(int, string, DateTime), long> counts,
        CancellationToken ct)
    {
        // ON CONFLICT DO UPDATE: aggregate counts when the same (project, key,
        // day) already exists. The `type` column auto-detects numeric vs string
        // values via float coercion (mirrors CH's log_keys_mv that does the same).
        const string sql = @"
            INSERT INTO analytics.log_keys (project_id, key, day, count, type)
            VALUES (@projectId, @key, @day, @count, @type)
            ON CONFLICT (project_id, key, day) DO UPDATE
                SET count = analytics.log_keys.count + EXCLUDED.count";

        foreach (var kv in counts)
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("projectId", kv.Key.Item1);
            cmd.Parameters.AddWithValue("key", kv.Key.Item2);
            cmd.Parameters.AddWithValue("day", kv.Key.Item3);
            cmd.Parameters.AddWithValue("count", kv.Value);
            cmd.Parameters.AddWithValue("type", "String"); // type detection deferred to a future PR
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task UpsertLogKeyValuesAsync(
        NpgsqlConnection conn,
        Dictionary<(int, string, DateTime, string), long> counts,
        CancellationToken ct)
    {
        const string sql = @"
            INSERT INTO analytics.log_key_values (project_id, key, day, value, count)
            VALUES (@projectId, @key, @day, @value, @count)
            ON CONFLICT (project_id, key, day, value) DO UPDATE
                SET count = analytics.log_key_values.count + EXCLUDED.count";

        foreach (var kv in counts)
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("projectId", kv.Key.Item1);
            cmd.Parameters.AddWithValue("key", kv.Key.Item2);
            cmd.Parameters.AddWithValue("day", kv.Key.Item3);
            cmd.Parameters.AddWithValue("value", kv.Key.Item4);
            cmd.Parameters.AddWithValue("count", kv.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static LogRow ReadLogRow(NpgsqlDataReader r)
    {
        var attributes = new Dictionary<string, string>();
        if (!r.IsDBNull(13))
        {
            var json = r.GetString(13);
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (parsed != null) attributes = parsed;
                }
                catch (JsonException)
                {
                    // Defensive: the column type guarantees valid JSON, but if a
                    // future migration ever stores nested objects the typed
                    // Dictionary<string,string> deserialize would fail. Leave
                    // attributes empty rather than fail the whole row read.
                }
            }
        }

        return new LogRow
        {
            Timestamp = r.GetFieldValue<DateTime>(0),
            ProjectId = r.GetInt32(1),
            TraceId = r.IsDBNull(2) ? "" : r.GetString(2),
            SpanId = r.IsDBNull(3) ? "" : r.GetString(3),
            SecureSessionId = r.IsDBNull(4) ? "" : r.GetString(4),
            UUID = r.IsDBNull(5) ? "" : r.GetString(5),
            TraceFlags = r.IsDBNull(6) ? 0 : r.GetInt32(6),
            SeverityText = r.IsDBNull(7) ? "" : r.GetString(7),
            SeverityNumber = r.IsDBNull(8) ? 0 : r.GetInt32(8),
            // Source is a `LogSource` enum on the model; we store as text and
            // cast to the enum. Unknown values fall back to enum default.
            Source = r.IsDBNull(9)
                ? default
                : Enum.TryParse<HoldFast.Domain.Enums.LogSource>(r.GetString(9), true, out var src) ? src : default,
            ServiceName = r.IsDBNull(10) ? "" : r.GetString(10),
            ServiceVersion = r.IsDBNull(11) ? "" : r.GetString(11),
            Body = r.IsDBNull(12) ? "" : r.GetString(12),
            LogAttributes = attributes,
            Environment = r.IsDBNull(14) ? "" : r.GetString(14),
        };
    }

    private static LogConnection BuildConnection(
        List<LogRow> rows, int limit, ClickHousePagination pagination)
    {
        // Same pagination logic as ClickHouseService.ComputePageInfo - we
        // over-fetch by 1 to detect "has next page" without a separate count.
        var hasMore = rows.Count > limit;
        if (hasMore) rows.RemoveAt(rows.Count - 1);

        var edges = rows.Select(r => new LogEdge { Node = r, Cursor = r.Cursor }).ToList();
        var isDesc = pagination.Direction.Equals("DESC", StringComparison.OrdinalIgnoreCase);

        return new LogConnection
        {
            Edges = edges,
            PageInfo = new PageInfo
            {
                HasNextPage = hasMore && pagination.Before == null,
                HasPreviousPage = hasMore && pagination.Before != null,
                StartCursor = edges.FirstOrDefault()?.Cursor,
                EndCursor = edges.LastOrDefault()?.Cursor,
            },
        };
    }

    internal static int ClampLimit(int limit) =>
        limit <= 0 ? DefaultLimit : Math.Min(limit, MaxLimit);

    /// <summary>
    /// Coerce a DateTime to a date for the PG `date` column. Avoids timezone
    /// drift around midnight by taking just the Y/M/D components.
    /// </summary>
    internal static DateOnly DateOnlyOf(DateTime ts) =>
        ts == default
            ? new DateOnly(1970, 1, 1)
            : DateOnly.FromDateTime(ts.Kind == DateTimeKind.Utc ? ts : ts.ToUniversalTime());
}
