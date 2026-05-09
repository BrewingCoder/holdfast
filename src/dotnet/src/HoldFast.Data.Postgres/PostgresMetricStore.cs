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
/// Postgres implementation of <see cref="IMetricStore"/>.
///
/// HOL-33. Two methods:
/// - ReadMetricsAsync: time-bucketed aggregation with selectable aggregator
///   (count, sum, avg, min, max, p50/p90/p95/p99, count_distinct).
/// - WriteMetricAsync: per-row INSERT (CH's signature is per-row, not batch).
/// </summary>
public sealed class PostgresMetricStore : IMetricStore
{
    private readonly PostgresAnalyticsOptions _options;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PostgresMetricStore> _logger;

    private const int DefaultHistogramBuckets = 48;

    public PostgresMetricStore(
        IOptions<PostgresAnalyticsOptions> options,
        IConfiguration configuration,
        ILogger<PostgresMetricStore> logger)
    {
        _options = options.Value;
        _configuration = configuration;
        _logger = logger;
    }

    private string ConnectionString =>
        _options.ConnectionString
        ?? _configuration.GetConnectionString("PostgreSQL")
        ?? throw new InvalidOperationException(
            "PostgresMetricStore: no connection string configured");

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    /// <summary>
    /// Map an aggregator name + column to a PG expression. Whitelist —
    /// untrusted GraphQL input doesn't compose into SQL anywhere.
    /// </summary>
    internal static string BuildAggregatorExpression(string aggregator, string column)
    {
        // column is already vetted by ResolveColumn below; aggregator returns
        // a hardcoded expression per case.
        var col = ResolveColumn(column);
        return (aggregator ?? "").ToUpperInvariant() switch
        {
            "COUNT" => "count(*)",
            "COUNT_DISTINCT" or "COUNTDISTINCT" => $"count(DISTINCT {col})",
            "SUM" => $"sum({col})",
            "AVG" => $"avg({col})",
            "MIN" => $"min({col})",
            "MAX" => $"max({col})",
            "P50" => $"percentile_cont(0.50) WITHIN GROUP (ORDER BY {col})",
            "P90" => $"percentile_cont(0.90) WITHIN GROUP (ORDER BY {col})",
            "P95" => $"percentile_cont(0.95) WITHIN GROUP (ORDER BY {col})",
            "P99" => $"percentile_cont(0.99) WITHIN GROUP (ORDER BY {col})",
            _ => "count(*)",
        };
    }

    /// <summary>
    /// Resolve a column name to a safe identifier. The metrics table only has
    /// a small set of columns the dashboard ever aggregates over; anything
    /// outside the whitelist falls back to metric_value (the obvious default).
    /// </summary>
    internal static string ResolveColumn(string? raw) =>
        raw?.ToLowerInvariant() switch
        {
            "value" or "metric_value" => "metric_value",
            "count" => "metric_value", // count(*) ignores the column anyway
            null or "" => "metric_value",
            _ => "metric_value", // unknown — fall back rather than allow injection
        };

    public async Task<MetricsBuckets> ReadMetricsAsync(
        int projectId, QueryInput query, string bucketBy,
        List<string>? groupBy, string aggregator, string? column,
        CancellationToken ct = default)
    {
        var aggExpr = BuildAggregatorExpression(aggregator, column ?? "metric_value");
        var nBuckets = DefaultHistogramBuckets;

        // groupBy is operator-supplied via GraphQL — sanitize each entry to
        // [A-Za-z0-9_] only. Anything else gets dropped.
        var sanitizedGroupBy = (groupBy ?? new List<string>())
            .Select(g => new string(g.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray()))
            .Where(g => !string.IsNullOrEmpty(g))
            .ToList();

        var sql = new System.Text.StringBuilder();
        var parameters = new Dictionary<string, object>
        {
            ["projectId"] = projectId,
            ["startDate"] = query.DateRange.StartDate,
            ["endDate"] = query.DateRange.EndDate,
        };

        if (string.Equals(bucketBy, "none", StringComparison.OrdinalIgnoreCase))
        {
            // No time bucketing — single result per group.
            var groupClause = sanitizedGroupBy.Count > 0
                ? string.Join(", ", sanitizedGroupBy.Select(g => $"tags ->> '{g}'"))
                : null;

            sql.Append($"SELECT {aggExpr} AS value, count(*)::bigint AS count");
            if (groupClause != null)
                sql.Append(", ").Append(groupClause).Append(" AS group_value");
            sql.Append(" FROM analytics.metrics");
            sql.Append(" WHERE project_id = @projectId");
            sql.Append(" AND timestamp >= @startDate AND timestamp <= @endDate");
            if (!string.IsNullOrWhiteSpace(query.Query))
            {
                sql.Append(" AND (metric_name ILIKE @q OR tags ->> 'service_name' ILIKE @q)");
                parameters["q"] = $"%{query.Query}%";
            }
            if (groupClause != null)
                sql.Append(" GROUP BY ").Append(groupClause);
            sql.Append(" ORDER BY value DESC LIMIT 100");
        }
        else
        {
            // Time-bucketed (default).
            sql.Append($@"
                SELECT
                    to_timestamp(
                        floor(extract(epoch FROM timestamp) /
                              GREATEST(1, (extract(epoch FROM @endDate) - extract(epoch FROM @startDate))::bigint / {nBuckets}))
                        * GREATEST(1, (extract(epoch FROM @endDate) - extract(epoch FROM @startDate))::bigint / {nBuckets})
                    ) AS bucket_start,
                    {aggExpr} AS value,
                    count(*)::bigint AS count");
            if (sanitizedGroupBy.Count > 0)
                sql.Append(", ").Append(string.Join(", ", sanitizedGroupBy.Select(g => $"tags ->> '{g}'"))).Append(" AS group_value");
            sql.Append(" FROM analytics.metrics");
            sql.Append(" WHERE project_id = @projectId");
            sql.Append(" AND timestamp >= @startDate AND timestamp <= @endDate");
            if (!string.IsNullOrWhiteSpace(query.Query))
            {
                sql.Append(" AND (metric_name ILIKE @q OR tags ->> 'service_name' ILIKE @q)");
                parameters["q"] = $"%{query.Query}%";
            }
            sql.Append(" GROUP BY bucket_start");
            if (sanitizedGroupBy.Count > 0)
                sql.Append(", ").Append(string.Join(", ", sanitizedGroupBy.Select(g => $"tags ->> '{g}'")));
            sql.Append(" ORDER BY bucket_start");
        }

        await using var conn = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql.ToString(), conn);
        foreach (var (n, v) in parameters) cmd.Parameters.AddWithValue(n, v);

        var buckets = new List<MetricsBucket>();
        long total = 0;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var bucket = new MetricsBucket();
            int idx = 0;
            if (!string.Equals(bucketBy, "none", StringComparison.OrdinalIgnoreCase))
            {
                bucket.BucketStart = reader.GetDateTime(idx++);
                bucket.BucketEnd = bucket.BucketStart;
            }
            bucket.Value = reader.IsDBNull(idx) ? 0 : Convert.ToDouble(reader.GetValue(idx));
            idx++;
            bucket.Count = reader.IsDBNull(idx) ? 0 : reader.GetInt64(idx);
            idx++;
            if (sanitizedGroupBy.Count > 0 && idx < reader.FieldCount)
            {
                bucket.Group = reader.IsDBNull(idx) ? null : reader.GetString(idx);
            }
            total += bucket.Count;
            buckets.Add(bucket);
        }

        return new MetricsBuckets { Buckets = buckets, TotalCount = total };
    }

    public async Task WriteMetricAsync(
        int projectId, string metricName, double metricValue,
        string? category, DateTime timestamp,
        Dictionary<string, string>? tags, string? sessionSecureId,
        CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO analytics.metrics (
                timestamp, project_id, metric_name, metric_value,
                category, tags, secure_session_id
            ) VALUES (@ts, @projectId, @name, @value, @category, @tags, @sessionId)";

        await using var conn = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("ts", NpgsqlDbType.TimestampTz, timestamp);
        cmd.Parameters.AddWithValue("projectId", projectId);
        cmd.Parameters.AddWithValue("name", metricName ?? "");
        cmd.Parameters.AddWithValue("value", metricValue);
        cmd.Parameters.AddWithValue("category", category ?? "");
        cmd.Parameters.AddWithValue("tags", NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(tags ?? new Dictionary<string, string>()));
        cmd.Parameters.AddWithValue("sessionId", sessionSecureId ?? "");
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
