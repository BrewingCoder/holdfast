using HoldFast.Analytics;
using HoldFast.Analytics.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace HoldFast.Data.Postgres;

/// <summary>
/// Postgres implementation of <see cref="ISessionAnalyticsStore"/>.
///
/// HOL-31. Sessions differ from logs/traces in shape — no JSONB attributes
/// bag, columns are explicit and stable. The reserved-keys list returned by
/// GetSessionsKeysAsync is hardcoded (matches CH); only GetSessionsKeyValues
/// hits the catalog table.
/// </summary>
public sealed class PostgresSessionAnalyticsStore : ISessionAnalyticsStore
{
    private readonly PostgresAnalyticsOptions _options;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PostgresSessionAnalyticsStore> _logger;

    private const int MaxLimit = 10_000;

    /// <summary>
    /// Reserved session field keys returned by <see cref="GetSessionsKeysAsync"/>.
    /// Mirrors ClickHouseService's hardcoded list — these correspond to the
    /// columns on analytics.sessions plus the device_id/fingerprint legacy
    /// holdovers from the SaaS schema (kept for API stability with the CH backend).
    /// </summary>
    private static readonly string[] ReservedKeys =
    {
        "identifier", "city", "state", "country", "os_name", "os_version",
        "browser_name", "browser_version", "environment", "device_id",
        "fingerprint", "has_errors", "has_rage_clicks", "pages_visited",
        "active_length", "length", "processed", "first_time", "viewed",
    };

    public PostgresSessionAnalyticsStore(
        IOptions<PostgresAnalyticsOptions> options,
        IConfiguration configuration,
        ILogger<PostgresSessionAnalyticsStore> logger)
    {
        _options = options.Value;
        _configuration = configuration;
        _logger = logger;
    }

    private string ConnectionString =>
        _options.ConnectionString
        ?? _configuration.GetConnectionString("PostgreSQL")
        ?? throw new InvalidOperationException(
            "PostgresSessionAnalyticsStore: no connection string configured");

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    // ── Reads ────────────────────────────────────────────────────────

    public async Task<List<HistogramBucket>> ReadSessionsHistogramAsync(
        int projectId, QueryInput query, CancellationToken ct = default)
    {
        // Hourly buckets matching CH's `toStartOfInterval(CreatedAt, INTERVAL 1 hour)`.
        // PG `date_trunc('hour', ...)` produces the same bucket boundaries.
        const string sql = @"
            SELECT date_trunc('hour', created_at) AS bucket_start,
                   date_trunc('hour', created_at) + INTERVAL '1 hour' AS bucket_end,
                   count(DISTINCT session_id)::bigint AS count
            FROM analytics.sessions
            WHERE project_id = @projectId
              AND created_at >= @startDate
              AND created_at <= @endDate
            GROUP BY bucket_start, bucket_end
            ORDER BY bucket_start";

        await using var conn = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("projectId", projectId);
        cmd.Parameters.AddWithValue("startDate", query.DateRange.StartDate);
        cmd.Parameters.AddWithValue("endDate", query.DateRange.EndDate);

        var buckets = new List<HistogramBucket>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            buckets.Add(new HistogramBucket
            {
                BucketStart = reader.GetDateTime(0),
                BucketEnd = reader.GetDateTime(1),
                Count = reader.GetInt64(2),
            });
        }
        return buckets;
    }

    public async Task<(List<int> Ids, long Total)> QuerySessionIdsAsync(
        int projectId, QueryInput query, int count, int page,
        string? sortField = null, bool sortDesc = true, CancellationToken ct = default)
    {
        // Count + IDs query. CH uses count(DISTINCT ID); PG matches.
        var (whereClause, parameters) = BuildSessionWhere(projectId, query);
        var sortColumn = NormalizeSortField(sortField);
        var dir = sortDesc ? "DESC" : "ASC";
        var limit = Math.Min(count, MaxLimit);
        var offset = page * count;

        await using var conn = await OpenAsync(ct);

        // Count
        long total;
        var countSql = $"SELECT count(DISTINCT session_id) FROM analytics.sessions WHERE {whereClause}";
        await using (var countCmd = new NpgsqlCommand(countSql, conn))
        {
            foreach (var (n, v) in parameters) countCmd.Parameters.AddWithValue(n, v);
            total = (long)(await countCmd.ExecuteScalarAsync(ct))!;
        }

        // IDs. PG rejects `SELECT DISTINCT col ORDER BY other_col` (the ORDER BY
        // expressions must appear in the DISTINCT projection). Switch to GROUP
        // BY + aggregate over the sort column — semantically equivalent for the
        // dashboard's "newest sessions first" use case (MAX of created_at gives
        // the latest occurrence per id, which is what users see in the list).
        var idsSql = $@"
            SELECT session_id, MAX({sortColumn}) AS sort_value
            FROM analytics.sessions
            WHERE {whereClause}
            GROUP BY session_id
            ORDER BY sort_value {dir}
            LIMIT @limit OFFSET @offset";
        var ids = new List<int>();
        await using (var cmd = new NpgsqlCommand(idsSql, conn))
        {
            foreach (var (n, v) in parameters) cmd.Parameters.AddWithValue(n, v);
            cmd.Parameters.AddWithValue("limit", limit);
            cmd.Parameters.AddWithValue("offset", offset);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                // session_id is BIGINT but the analytics-layer surface returns int
                // (matches CH's signature). Truncate is safe — session IDs come
                // from Postgres serial which fits int range for the foreseeable.
                ids.Add((int)reader.GetInt64(0));
            }
        }

        return (ids, total);
    }

    private static (string Clause, List<(string, object)> Params) BuildSessionWhere(
        int projectId, QueryInput query)
    {
        var p = new List<(string, object)> { ("projectId", projectId) };
        var clause = new System.Text.StringBuilder("project_id = @projectId");

        if (query.DateRange.StartDate != default)
        {
            clause.Append(" AND created_at >= @startDate");
            p.Add(("startDate", query.DateRange.StartDate));
        }
        if (query.DateRange.EndDate != default)
        {
            clause.Append(" AND created_at <= @endDate");
            p.Add(("endDate", query.DateRange.EndDate));
        }

        // Same simplified filter as CH — search by identifier or environment
        if (!string.IsNullOrWhiteSpace(query.Query))
        {
            clause.Append(" AND (identifier ILIKE @q OR environment ILIKE @q)");
            p.Add(("q", $"%{query.Query}%"));
        }

        return (clause.ToString(), p);
    }

    private static string NormalizeSortField(string? sortField) =>
        // Map common GraphQL snake_case fields to actual columns. Defaults to
        // created_at to match CH's NormalizeSortField default.
        sortField switch
        {
            "created_at" or "createdAt" or null or "" => "created_at",
            "active_length" or "activeLength" => "active_length",
            "length" => "length",
            "pages_visited" or "pagesVisited" => "pages_visited",
            // Defensive default: untrusted input (operator-supplied via GraphQL)
            // shouldn't directly compose into ORDER BY. Anything not in the
            // whitelist falls back to created_at.
            _ => "created_at",
        };

    public Task<List<QueryKey>> GetSessionsKeysAsync(
        int projectId, DateTime startDate, DateTime endDate, string? query,
        CancellationToken ct = default)
    {
        // Hardcoded list — matches CH ClickHouseService implementation.
        // Filter by query substring if provided (same case-insensitive Contains).
        var keys = ReservedKeys
            .Where(k => string.IsNullOrEmpty(query)
                     || k.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(k => new QueryKey { Name = k, Type = "String" })
            .ToList();
        return Task.FromResult(keys);
    }

    public async Task<List<string>> GetSessionsKeyValuesAsync(
        int projectId, string keyName, DateTime startDate, DateTime endDate,
        string? query, int? count, CancellationToken ct = default)
    {
        var limit = count ?? 10;

        // CH queried a shared `fields` table populated by Go worker code that
        // doesn't exist in the .NET migration. We use a per-domain catalog
        // (analytics.session_field_values) populated inline by WriteSessionsAsync.
        const string sql = @"
            SELECT DISTINCT value
            FROM analytics.session_field_values
            WHERE project_id = @projectId
              AND key = @keyName
              AND day >= @startDay
              AND day <= @endDay
            LIMIT @limit";

        await using var conn = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("projectId", projectId);
        cmd.Parameters.AddWithValue("keyName", keyName);
        cmd.Parameters.AddWithValue("startDay", DateOnlyOf(startDate));
        cmd.Parameters.AddWithValue("endDay", DateOnlyOf(endDate));
        cmd.Parameters.AddWithValue("limit", limit);

        var values = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            values.Add(reader.GetString(0));
        return values;
    }

    // ── Writes ───────────────────────────────────────────────────────

    public async Task WriteSessionsAsync(IEnumerable<SessionRowInput> sessions, CancellationToken ct = default)
    {
        var batch = sessions.ToList();
        if (batch.Count == 0) return;

        await using var conn = await OpenAsync(ct);

        // Bulk insert via binary COPY.
        await using (var importer = await conn.BeginBinaryImportAsync(@"
            COPY analytics.sessions (
                created_at, project_id, session_id, secure_session_id, identifier,
                os_name, os_version, browser_name, browser_version,
                city, state, country, environment, app_version, service_name,
                active_length, length, pages_visited,
                has_errors, has_rage_clicks, processed, first_time
            ) FROM STDIN (FORMAT BINARY)", ct))
        {
            foreach (var s in batch)
            {
                await importer.StartRowAsync(ct);
                await importer.WriteAsync(s.CreatedAt, NpgsqlDbType.TimestampTz, ct);
                await importer.WriteAsync(s.ProjectId, NpgsqlDbType.Integer, ct);
                await importer.WriteAsync((long)s.SessionId, NpgsqlDbType.Bigint, ct);
                await importer.WriteAsync(s.SecureSessionId ?? "", NpgsqlDbType.Text, ct);
                await importer.WriteAsync(s.Identifier ?? "", NpgsqlDbType.Text, ct);
                await importer.WriteAsync(s.OSName ?? "", NpgsqlDbType.Text, ct);
                await importer.WriteAsync(s.OSVersion ?? "", NpgsqlDbType.Text, ct);
                await importer.WriteAsync(s.BrowserName ?? "", NpgsqlDbType.Text, ct);
                await importer.WriteAsync(s.BrowserVersion ?? "", NpgsqlDbType.Text, ct);
                await importer.WriteAsync(s.City ?? "", NpgsqlDbType.Text, ct);
                await importer.WriteAsync(s.State ?? "", NpgsqlDbType.Text, ct);
                await importer.WriteAsync(s.Country ?? "", NpgsqlDbType.Text, ct);
                await importer.WriteAsync(s.Environment ?? "", NpgsqlDbType.Text, ct);
                await importer.WriteAsync(s.AppVersion ?? "", NpgsqlDbType.Text, ct);
                await importer.WriteAsync(s.ServiceName ?? "", NpgsqlDbType.Text, ct);
                await importer.WriteAsync(s.ActiveLength, NpgsqlDbType.Integer, ct);
                await importer.WriteAsync(s.Length, NpgsqlDbType.Integer, ct);
                await importer.WriteAsync(s.PagesVisited, NpgsqlDbType.Integer, ct);
                await importer.WriteAsync(s.HasErrors, NpgsqlDbType.Boolean, ct);
                await importer.WriteAsync(s.HasRageClicks, NpgsqlDbType.Boolean, ct);
                await importer.WriteAsync(s.Processed, NpgsqlDbType.Boolean, ct);
                await importer.WriteAsync(s.FirstTime, NpgsqlDbType.Boolean, ct);
            }
            await importer.CompleteAsync(ct);
        }

        // Catalog upserts for the reserved-keys autocomplete. Map each session's
        // string-typed columns to (key, value) tuples and aggregate counts.
        var counts = new Dictionary<(int ProjectId, string Key, DateTime Day, string Value), long>();
        foreach (var s in batch)
        {
            var day = s.CreatedAt.Date;
            void Add(string key, string? value)
            {
                if (string.IsNullOrEmpty(value)) return;
                var tup = (s.ProjectId, key, day, value);
                counts[tup] = counts.GetValueOrDefault(tup) + 1;
            }
            Add("identifier", s.Identifier);
            Add("os_name", s.OSName);
            Add("os_version", s.OSVersion);
            Add("browser_name", s.BrowserName);
            Add("browser_version", s.BrowserVersion);
            Add("city", s.City);
            Add("state", s.State);
            Add("country", s.Country);
            Add("environment", s.Environment);
        }

        if (counts.Count == 0) return;

        const string upsertSql = @"
            INSERT INTO analytics.session_field_values (project_id, key, day, value, count)
            VALUES (@projectId, @key, @day, @value, @count)
            ON CONFLICT (project_id, key, day, value) DO UPDATE
                SET count = analytics.session_field_values.count + EXCLUDED.count";

        foreach (var kv in counts)
        {
            await using var cmd = new NpgsqlCommand(upsertSql, conn);
            cmd.Parameters.AddWithValue("projectId", kv.Key.Item1);
            cmd.Parameters.AddWithValue("key", kv.Key.Item2);
            cmd.Parameters.AddWithValue("day", kv.Key.Item3);
            cmd.Parameters.AddWithValue("value", kv.Key.Item4);
            cmd.Parameters.AddWithValue("count", kv.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────

    internal static DateOnly DateOnlyOf(DateTime ts) =>
        ts == default
            ? new DateOnly(1970, 1, 1)
            : DateOnly.FromDateTime(ts.Kind == DateTimeKind.Utc ? ts : ts.ToUniversalTime());

    internal static string ResolveSortField(string? raw) => NormalizeSortField(raw);
}
