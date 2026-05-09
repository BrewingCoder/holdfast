using System.Text.Json;
using HoldFast.Analytics;
using HoldFast.Analytics.Models;
using HoldFast.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace HoldFast.Data.Postgres;

/// <summary>
/// Postgres implementation of <see cref="ITraceStore"/>.
///
/// HOL-30: mirrors <c>HoldFast.Data.ClickHouse.ClickHouseService</c>'s trace
/// methods translated to Postgres + TimescaleDB. Same shape as
/// <see cref="PostgresLogStore"/> from HOL-29 — same cursor format, same
/// query-filter pattern, same inline catalog-upsert strategy.
///
/// Notable parities with the CH impl:
/// - Events / Links columns exist on the schema but are NOT read or written.
///   The CH service has the same gap (its comment notes parallel-array Nested
///   reads aren't implemented). Future PR can populate them once the OTLP
///   ingest path stops dropping them.
/// - <c>omitBody</c> parameter is accepted but unused — matches CH where the
///   parameter is also a no-op.
/// </summary>
public sealed class PostgresTraceStore : ITraceStore
{
    private readonly PostgresAnalyticsOptions _options;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PostgresTraceStore> _logger;

    private const int DefaultLimit = 50;
    private const int MaxLimit = 10_000;
    private const int DefaultHistogramBuckets = 48;

    public PostgresTraceStore(
        IOptions<PostgresAnalyticsOptions> options,
        IConfiguration configuration,
        ILogger<PostgresTraceStore> logger)
    {
        _options = options.Value;
        _configuration = configuration;
        _logger = logger;
    }

    private string ConnectionString =>
        _options.ConnectionString
        ?? _configuration.GetConnectionString("PostgreSQL")
        ?? throw new InvalidOperationException(
            "PostgresTraceStore: no connection string configured (PostgresAnalytics:ConnectionString or ConnectionStrings:PostgreSQL)");

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    // ── Reads ────────────────────────────────────────────────────────

    public async Task<TraceConnection> ReadTracesAsync(
        int projectId, QueryInput query, ClickHousePagination pagination,
        bool omitBody = false, CancellationToken ct = default)
    {
        var limit = ClampLimit(pagination.Limit);
        var isDesc = pagination.Direction.Equals("DESC", StringComparison.OrdinalIgnoreCase);

        var (sql, parameters) = BuildTracesReadQuery(projectId, query, pagination, limit, isDesc);

        await using var conn = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);

        var rows = new List<TraceRow>();
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                rows.Add(ReadTraceRow(reader));
        }

        return BuildConnection(rows, limit, pagination);
    }

    private static (string Sql, List<(string, object)> Params) BuildTracesReadQuery(
        int projectId, QueryInput query, ClickHousePagination pagination, int limit, bool isDesc)
    {
        var dir = isDesc ? "DESC" : "ASC";
        var sql = new System.Text.StringBuilder();
        var p = new List<(string, object)>();

        sql.AppendLine(@"
            SELECT timestamp, uuid::text, trace_id, span_id, parent_span_id, trace_state,
                   span_name, span_kind, service_name, service_version, trace_attributes,
                   duration, status_code, status_message, project_id, secure_session_id,
                   environment, has_errors
            FROM analytics.traces
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

        if (!string.IsNullOrWhiteSpace(query.Query))
        {
            // Same simplified filter as the CH impl, which itself notes that full
            // query-language parsing is deferred. Match span_name + service_name +
            // attributes.service_name for the common dashboard search.
            sql.AppendLine(@"
                AND (span_name ILIKE @q
                  OR service_name ILIKE @q
                  OR trace_attributes ->> 'service_name' ILIKE @q)");
            p.Add(("q", $"%{query.Query}%"));
        }

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

    public async Task<List<HistogramBucket>> ReadTracesHistogramAsync(
        int projectId, QueryInput query, CancellationToken ct = default)
    {
        const int nBuckets = DefaultHistogramBuckets;

        var sql = new System.Text.StringBuilder();
        var parameters = new List<(string, object)>();

        sql.AppendLine($@"
            SELECT
                to_timestamp(
                    floor(extract(epoch FROM timestamp) /
                          GREATEST(1, (extract(epoch FROM @endDate) - extract(epoch FROM @startDate))::bigint / {nBuckets}))
                    * GREATEST(1, (extract(epoch FROM @endDate) - extract(epoch FROM @startDate))::bigint / {nBuckets})
                ) AS bucket_start,
                count(*)::bigint AS count,
                span_name AS group_value
            FROM analytics.traces
            WHERE project_id = @projectId
              AND timestamp >= @startDate
              AND timestamp <= @endDate");
        parameters.Add(("projectId", projectId));
        parameters.Add(("startDate", query.DateRange.StartDate));
        parameters.Add(("endDate", query.DateRange.EndDate));

        if (!string.IsNullOrWhiteSpace(query.Query))
        {
            sql.AppendLine(@"
                AND (span_name ILIKE @q
                  OR service_name ILIKE @q
                  OR trace_attributes ->> 'service_name' ILIKE @q)");
            parameters.Add(("q", $"%{query.Query}%"));
        }

        sql.AppendLine("GROUP BY bucket_start, span_name");
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
                BucketEnd = reader.GetDateTime(0),
                Count = reader.GetInt64(1),
                Group = reader.IsDBNull(2) ? null : reader.GetString(2),
            });
        }
        return buckets;
    }

    public async Task<List<string>> GetTraceKeysAsync(
        int projectId, QueryInput query, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT DISTINCT key FROM analytics.trace_keys
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

    public async Task<List<string>> GetTraceKeyValuesAsync(
        int projectId, string key, QueryInput query, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT DISTINCT value FROM analytics.trace_key_values
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

    // ── Writes ───────────────────────────────────────────────────────

    public async Task WriteTracesAsync(IEnumerable<TraceRowInput> traces, CancellationToken ct = default)
    {
        var batch = traces.ToList();
        if (batch.Count == 0) return;

        await using var conn = await OpenAsync(ct);

        await using (var importer = await conn.BeginBinaryImportAsync(@"
            COPY analytics.traces (
                timestamp, uuid, project_id, trace_id, span_id, parent_span_id,
                secure_session_id, span_name, span_kind, duration, service_name,
                service_version, trace_attributes, status_code, status_message,
                environment, has_errors
            ) FROM STDIN (FORMAT BINARY)", ct))
        {
            foreach (var t in batch)
            {
                await importer.StartRowAsync(ct);
                await importer.WriteAsync(t.Timestamp, NpgsqlDbType.TimestampTz, ct);
                await importer.WriteAsync(Guid.NewGuid(), NpgsqlDbType.Uuid, ct);
                await importer.WriteAsync(t.ProjectId, NpgsqlDbType.Integer, ct);
                await importer.WriteAsync(t.TraceId ?? "", NpgsqlDbType.Text, ct);
                await importer.WriteAsync(t.SpanId ?? "", NpgsqlDbType.Text, ct);
                await importer.WriteAsync(t.ParentSpanId ?? "", NpgsqlDbType.Text, ct);
                await importer.WriteAsync(t.SecureSessionId ?? "", NpgsqlDbType.Text, ct);
                await importer.WriteAsync(t.SpanName ?? "", NpgsqlDbType.Text, ct);
                await importer.WriteAsync(t.SpanKind ?? "", NpgsqlDbType.Text, ct);
                await importer.WriteAsync(t.Duration, NpgsqlDbType.Bigint, ct);
                await importer.WriteAsync(t.ServiceName ?? "", NpgsqlDbType.Text, ct);
                await importer.WriteAsync(t.ServiceVersion ?? "", NpgsqlDbType.Text, ct);
                await importer.WriteAsync(
                    JsonSerializer.Serialize(t.TraceAttributes ?? new()),
                    NpgsqlDbType.Jsonb, ct);
                await importer.WriteAsync(t.StatusCode ?? "", NpgsqlDbType.Text, ct);
                await importer.WriteAsync(t.StatusMessage ?? "", NpgsqlDbType.Text, ct);
                await importer.WriteAsync(t.Environment ?? "", NpgsqlDbType.Text, ct);
                await importer.WriteAsync(t.HasErrors, NpgsqlDbType.Boolean, ct);
            }
            await importer.CompleteAsync(ct);
        }

        // Catalog upserts — same structure as PostgresLogStore. Aggregate per
        // tuple in memory before issuing UPSERTs.
        var keyCounts = new Dictionary<(int ProjectId, string Key, DateTime Day), long>();
        var kvCounts = new Dictionary<(int ProjectId, string Key, DateTime Day, string Value), long>();

        foreach (var t in batch)
        {
            if (t.TraceAttributes == null) continue;
            var day = t.Timestamp.Date;
            foreach (var kv in t.TraceAttributes)
            {
                var keyTuple = (t.ProjectId, kv.Key, day);
                keyCounts[keyTuple] = keyCounts.GetValueOrDefault(keyTuple) + 1;

                var kvTuple = (t.ProjectId, kv.Key, day, kv.Value);
                kvCounts[kvTuple] = kvCounts.GetValueOrDefault(kvTuple) + 1;
            }
        }

        if (keyCounts.Count > 0)
            await UpsertTraceKeysAsync(conn, keyCounts, ct);
        if (kvCounts.Count > 0)
            await UpsertTraceKeyValuesAsync(conn, kvCounts, ct);
    }

    private static async Task UpsertTraceKeysAsync(
        NpgsqlConnection conn,
        Dictionary<(int, string, DateTime), long> counts,
        CancellationToken ct)
    {
        const string sql = @"
            INSERT INTO analytics.trace_keys (project_id, key, day, count, type)
            VALUES (@projectId, @key, @day, @count, @type)
            ON CONFLICT (project_id, key, day) DO UPDATE
                SET count = analytics.trace_keys.count + EXCLUDED.count";

        foreach (var kv in counts)
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("projectId", kv.Key.Item1);
            cmd.Parameters.AddWithValue("key", kv.Key.Item2);
            cmd.Parameters.AddWithValue("day", kv.Key.Item3);
            cmd.Parameters.AddWithValue("count", kv.Value);
            cmd.Parameters.AddWithValue("type", "String");
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task UpsertTraceKeyValuesAsync(
        NpgsqlConnection conn,
        Dictionary<(int, string, DateTime, string), long> counts,
        CancellationToken ct)
    {
        const string sql = @"
            INSERT INTO analytics.trace_key_values (project_id, key, day, value, count)
            VALUES (@projectId, @key, @day, @value, @count)
            ON CONFLICT (project_id, key, day, value) DO UPDATE
                SET count = analytics.trace_key_values.count + EXCLUDED.count";

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

    private static TraceRow ReadTraceRow(NpgsqlDataReader r)
    {
        var attributes = new Dictionary<string, string>();
        if (!r.IsDBNull(10))
        {
            var json = r.GetString(10);
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (parsed != null) attributes = parsed;
                }
                catch (JsonException)
                {
                    // Defensive: future migrations might store nested objects in
                    // trace_attributes; fall back to empty dict rather than fail
                    // the row read.
                }
            }
        }

        // SpanKind is a Domain.Enums.SpanKind enum on the model; CH stores as
        // text and we mirror that here. Unknown values fall back to enum
        // default (typically Unspecified).
        var spanKindText = r.IsDBNull(7) ? "" : r.GetString(7);
        var spanKind = Enum.TryParse<SpanKind>(spanKindText, true, out var sk)
            ? sk : default;

        return new TraceRow
        {
            Timestamp = r.GetFieldValue<DateTime>(0),
            UUID = r.IsDBNull(1) ? "" : r.GetString(1),
            TraceId = r.IsDBNull(2) ? "" : r.GetString(2),
            SpanId = r.IsDBNull(3) ? "" : r.GetString(3),
            ParentSpanId = r.IsDBNull(4) ? "" : r.GetString(4),
            TraceState = r.IsDBNull(5) ? "" : r.GetString(5),
            SpanName = r.IsDBNull(6) ? "" : r.GetString(6),
            SpanKind = spanKind,
            ServiceName = r.IsDBNull(8) ? "" : r.GetString(8),
            ServiceVersion = r.IsDBNull(9) ? "" : r.GetString(9),
            TraceAttributes = attributes,
            Duration = r.IsDBNull(11) ? 0 : r.GetInt64(11),
            StatusCode = r.IsDBNull(12) ? "" : r.GetString(12),
            StatusMessage = r.IsDBNull(13) ? "" : r.GetString(13),
            ProjectId = r.IsDBNull(14) ? 0 : r.GetInt32(14),
            SecureSessionId = r.IsDBNull(15) ? "" : r.GetString(15),
            Environment = r.IsDBNull(16) ? "" : r.GetString(16),
            HasErrors = !r.IsDBNull(17) && r.GetBoolean(17),
            // Events + Links left empty — same gap as the CH impl (parallel-array
            // reads not implemented; will revisit when OTLP ingest stops dropping
            // them on the floor).
            Events = [],
            Links = [],
        };
    }

    private static TraceConnection BuildConnection(
        List<TraceRow> rows, int limit, ClickHousePagination pagination)
    {
        var hasMore = rows.Count > limit;
        if (hasMore) rows.RemoveAt(rows.Count - 1);

        var edges = rows.Select(r => new TraceEdge { Node = r, Cursor = r.Cursor }).ToList();

        return new TraceConnection
        {
            Edges = edges,
            PageInfo = new PageInfo
            {
                HasNextPage = hasMore && pagination.Before == null,
                HasPreviousPage = hasMore && pagination.Before != null,
                StartCursor = edges.FirstOrDefault()?.Cursor,
                EndCursor = edges.LastOrDefault()?.Cursor,
            },
            Sampled = false,
        };
    }

    internal static int ClampLimit(int limit) =>
        limit <= 0 ? DefaultLimit : Math.Min(limit, MaxLimit);

    internal static DateOnly DateOnlyOf(DateTime ts) =>
        ts == default
            ? new DateOnly(1970, 1, 1)
            : DateOnly.FromDateTime(ts.Kind == DateTimeKind.Utc ? ts : ts.ToUniversalTime());
}
