using HoldFast.Analytics;
using HoldFast.Analytics.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace HoldFast.Data.Postgres;

/// <summary>
/// Postgres implementation of <see cref="IErrorAnalyticsStore"/>.
///
/// HOL-32. Errors are split across two tables:
/// - analytics.error_groups (one row per dedup key, updated on recurrence)
/// - analytics.error_objects (one row per occurrence, hypertable)
///
/// Like sessions, the keys list is hardcoded; the values lookup queries
/// the relevant column directly via a sanitized column-name whitelist
/// (mirrors CH's SanitizeColumnName approach).
/// </summary>
public sealed class PostgresErrorAnalyticsStore : IErrorAnalyticsStore
{
    private readonly PostgresAnalyticsOptions _options;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PostgresErrorAnalyticsStore> _logger;

    private const int MaxLimit = 10_000;
    private const int DefaultHistogramBuckets = 48;

    /// <summary>
    /// Reserved error keys returned by GetErrorsKeysAsync. Mirrors CH's list.
    /// </summary>
    private static readonly string[] ReservedKeys =
    {
        "event", "type", "url", "source", "stackTrace", "timestamp",
        "os", "browser", "environment", "service_name", "service_version",
    };

    public PostgresErrorAnalyticsStore(
        IOptions<PostgresAnalyticsOptions> options,
        IConfiguration configuration,
        ILogger<PostgresErrorAnalyticsStore> logger)
    {
        _options = options.Value;
        _configuration = configuration;
        _logger = logger;
    }

    private string ConnectionString =>
        _options.ConnectionString
        ?? _configuration.GetConnectionString("PostgreSQL")
        ?? throw new InvalidOperationException(
            "PostgresErrorAnalyticsStore: no connection string configured");

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    // ── Reads ────────────────────────────────────────────────────────

    public async Task<(List<int> Ids, long Total)> QueryErrorGroupIdsAsync(
        int projectId, QueryInput query, int count, int page, CancellationToken ct = default)
    {
        // Mirrors CH: count(DISTINCT ErrorGroupID) + GROUP BY id ORDER BY count DESC.
        var (whereClause, parameters) = BuildErrorObjectsWhere(projectId, query);

        await using var conn = await OpenAsync(ct);

        long total;
        await using (var countCmd = new NpgsqlCommand(
            $"SELECT count(DISTINCT error_group_id) FROM analytics.error_objects WHERE {whereClause}",
            conn))
        {
            foreach (var (n, v) in parameters) countCmd.Parameters.AddWithValue(n, v);
            total = (long)(await countCmd.ExecuteScalarAsync(ct))!;
        }

        var limit = Math.Min(count, MaxLimit);
        var offset = page * count;

        var idsSql = $@"
            SELECT error_group_id, count(*) AS cnt
            FROM analytics.error_objects
            WHERE {whereClause}
            GROUP BY error_group_id
            ORDER BY cnt DESC
            LIMIT @limit OFFSET @offset";

        var ids = new List<int>();
        await using (var cmd = new NpgsqlCommand(idsSql, conn))
        {
            foreach (var (n, v) in parameters) cmd.Parameters.AddWithValue(n, v);
            cmd.Parameters.AddWithValue("limit", limit);
            cmd.Parameters.AddWithValue("offset", offset);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                ids.Add(reader.GetInt32(0));
        }

        return (ids, total);
    }

    private static (string Clause, List<(string, object)> Params) BuildErrorObjectsWhere(
        int projectId, QueryInput query)
    {
        var p = new List<(string, object)> { ("projectId", projectId) };
        var clause = new System.Text.StringBuilder("project_id = @projectId");

        if (query.DateRange.StartDate != default)
        {
            clause.Append(" AND timestamp >= @startDate");
            p.Add(("startDate", query.DateRange.StartDate));
        }
        if (query.DateRange.EndDate != default)
        {
            clause.Append(" AND timestamp <= @endDate");
            p.Add(("endDate", query.DateRange.EndDate));
        }

        return (clause.ToString(), p);
    }

    public async Task<List<HistogramBucket>> ReadErrorObjectsHistogramAsync(
        int projectId, QueryInput query, CancellationToken ct = default)
    {
        const int nBuckets = DefaultHistogramBuckets;

        // Same equal-width bucket math as logs/traces.
        var sql = $@"
            SELECT
                to_timestamp(
                    floor(extract(epoch FROM timestamp) /
                          GREATEST(1, (extract(epoch FROM @endDate) - extract(epoch FROM @startDate))::bigint / {nBuckets}))
                    * GREATEST(1, (extract(epoch FROM @endDate) - extract(epoch FROM @startDate))::bigint / {nBuckets})
                ) AS bucket_start,
                count(*)::bigint AS count
            FROM analytics.error_objects
            WHERE project_id = @projectId
              AND timestamp >= @startDate
              AND timestamp <= @endDate
            GROUP BY bucket_start
            ORDER BY bucket_start ASC";

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
                BucketEnd = reader.GetDateTime(0),
                Count = reader.GetInt64(1),
            });
        }
        return buckets;
    }

    public Task<List<QueryKey>> GetErrorsKeysAsync(
        int projectId, DateTime startDate, DateTime endDate, string? query,
        CancellationToken ct = default)
    {
        var keys = ReservedKeys
            .Where(k => string.IsNullOrEmpty(query)
                     || k.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(k => new QueryKey { Name = k, Type = "String" })
            .ToList();
        return Task.FromResult(keys);
    }

    public async Task<List<string>> GetErrorsKeyValuesAsync(
        int projectId, string keyName, DateTime startDate, DateTime endDate,
        string? query, int? count, CancellationToken ct = default)
    {
        var limit = count ?? 10;

        // CH SanitizeColumnName resolves a key to a column name. Match the
        // same whitelist; unknown keys return empty (CH errors). The whitelist
        // keys correspond to columns on error_groups OR error_objects; we pick
        // the right table per key.
        var resolved = SanitizeColumnName(keyName);
        if (resolved is null) return [];

        var (table, column) = resolved.Value;
        var sql = $@"
            SELECT DISTINCT {column}
            FROM analytics.{table}
            WHERE project_id = @projectId
              AND {(table == "error_groups" ? "created_at" : "timestamp")} >= @startDate
              AND {(table == "error_groups" ? "created_at" : "timestamp")} <= @endDate
              AND {column} <> ''
            LIMIT @limit";

        await using var conn = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("projectId", projectId);
        cmd.Parameters.AddWithValue("startDate", startDate == default ? DateTime.UnixEpoch : startDate);
        cmd.Parameters.AddWithValue("endDate", endDate == default ? DateTime.UtcNow.AddYears(10) : endDate);
        cmd.Parameters.AddWithValue("limit", limit);

        var values = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (!reader.IsDBNull(0))
                values.Add(reader.GetString(0));
        }
        return values;
    }

    /// <summary>
    /// Resolve a reserved-key name to its (table, column) location. Whitelist
    /// keeps untrusted GraphQL input out of the SQL string. Returns null for
    /// unknown keys.
    /// </summary>
    internal static (string Table, string Column)? SanitizeColumnName(string keyName) =>
        keyName?.ToLowerInvariant() switch
        {
            "event" => ("error_groups", "event"),
            "type" => ("error_groups", "type"),
            "service_name" or "servicename" => ("error_groups", "service_name"),
            "url" or "visitedurl" => ("error_objects", "url"),
            "os" or "osname" => ("error_objects", "os"),
            "browser" => ("error_objects", "browser"),
            "environment" => ("error_objects", "environment"),
            "service_version" or "serviceversion" => ("error_objects", "service_version"),
            // "source", "stackTrace", "timestamp" appear in the keys list for
            // GraphQL surface compatibility but aren't queryable columns.
            // Returning null mirrors the "no values" outcome.
            _ => null,
        };

    // ── Writes ───────────────────────────────────────────────────────

    public async Task WriteErrorGroupsAsync(IEnumerable<ErrorGroupRowInput> errorGroups, CancellationToken ct = default)
    {
        var batch = errorGroups.ToList();
        if (batch.Count == 0) return;

        await using var conn = await OpenAsync(ct);

        // Use UPSERT instead of binary COPY because error_groups uses the
        // ReplacingMergeTree pattern in CH (re-insert with newer UpdatedAt
        // wins). PG analog: ON CONFLICT (project_id, error_group_id) DO UPDATE.
        const string sql = @"
            INSERT INTO analytics.error_groups (
                project_id, error_group_id, secure_id, created_at, updated_at,
                event, type, state, service_name, environments
            )
            VALUES (@projectId, @groupId, @secureId, @createdAt, @updatedAt,
                    @event, @type, @state, @serviceName, @environments)
            ON CONFLICT (project_id, error_group_id) DO UPDATE SET
                updated_at = EXCLUDED.updated_at,
                event = EXCLUDED.event,
                type = EXCLUDED.type,
                state = EXCLUDED.state,
                service_name = EXCLUDED.service_name,
                environments = EXCLUDED.environments
            WHERE analytics.error_groups.updated_at <= EXCLUDED.updated_at";

        foreach (var g in batch)
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("projectId", g.ProjectId);
            cmd.Parameters.AddWithValue("groupId", g.ErrorGroupId);
            cmd.Parameters.AddWithValue("secureId", g.SecureId ?? "");
            cmd.Parameters.AddWithValue("createdAt", g.CreatedAt);
            cmd.Parameters.AddWithValue("updatedAt", g.UpdatedAt);
            cmd.Parameters.AddWithValue("event", g.Event ?? "");
            cmd.Parameters.AddWithValue("type", g.Type ?? "");
            cmd.Parameters.AddWithValue("state", g.State ?? "OPEN");
            cmd.Parameters.AddWithValue("serviceName", g.ServiceName ?? "");
            cmd.Parameters.AddWithValue("environments", g.Environments ?? "");
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task WriteErrorObjectsAsync(IEnumerable<ErrorObjectRowInput> errorObjects, CancellationToken ct = default)
    {
        var batch = errorObjects.ToList();
        if (batch.Count == 0) return;

        await using var conn = await OpenAsync(ct);

        // error_objects is append-only (one row per occurrence) — bulk COPY.
        await using var importer = await conn.BeginBinaryImportAsync(@"
            COPY analytics.error_objects (
                timestamp, project_id, error_object_id, error_group_id,
                event, type, url, environment, os, browser,
                service_name, service_version
            ) FROM STDIN (FORMAT BINARY)", ct);

        foreach (var e in batch)
        {
            await importer.StartRowAsync(ct);
            await importer.WriteAsync(e.Timestamp, NpgsqlDbType.TimestampTz, ct);
            await importer.WriteAsync(e.ProjectId, NpgsqlDbType.Integer, ct);
            await importer.WriteAsync((long)e.ErrorObjectId, NpgsqlDbType.Bigint, ct);
            await importer.WriteAsync(e.ErrorGroupId, NpgsqlDbType.Integer, ct);
            await importer.WriteAsync(e.Event ?? "", NpgsqlDbType.Text, ct);
            await importer.WriteAsync(e.Type ?? "", NpgsqlDbType.Text, ct);
            await importer.WriteAsync(e.Url ?? "", NpgsqlDbType.Text, ct);
            await importer.WriteAsync(e.Environment ?? "", NpgsqlDbType.Text, ct);
            await importer.WriteAsync(e.OS ?? "", NpgsqlDbType.Text, ct);
            await importer.WriteAsync(e.Browser ?? "", NpgsqlDbType.Text, ct);
            await importer.WriteAsync(e.ServiceName ?? "", NpgsqlDbType.Text, ct);
            await importer.WriteAsync(e.ServiceVersion ?? "", NpgsqlDbType.Text, ct);
        }
        await importer.CompleteAsync(ct);
    }
}
