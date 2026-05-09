using HoldFast.Analytics.Models;
using HoldFast.Data.Postgres;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace HoldFast.Data.Tests.Postgres;

/// <summary>
/// HOL-32: live integration tests for PostgresErrorAnalyticsStore.
/// </summary>
[Trait("Category", "PgIntegration")]
public class PostgresErrorAnalyticsStoreIntegrationTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";

    private PostgresErrorAnalyticsStore _store = null!;
    private bool _pgReachable;

    public async Task InitializeAsync()
    {
        try
        {
            await using var probe = new NpgsqlConnection(ConnectionString);
            await probe.OpenAsync();
            await using var cmd = probe.CreateCommand();
            cmd.CommandText = "SELECT to_regclass('analytics.error_objects') IS NOT NULL";
            _pgReachable = (bool)(await cmd.ExecuteScalarAsync())!;
        }
        catch
        {
            _pgReachable = false;
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgreSQL"] = ConnectionString,
            })
            .Build();
        var opts = Options.Create(new PostgresAnalyticsOptions { Schema = "analytics" });
        _store = new PostgresErrorAnalyticsStore(opts, config, NullLogger<PostgresErrorAnalyticsStore>.Instance);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static int NextProjectId() =>
        999_999_900 + (int)(DateTime.UtcNow.Ticks % 1_000);

    private static async Task CleanupAsync(int projectId)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        foreach (var sql in new[]
        {
            "DELETE FROM analytics.error_groups WHERE project_id = @p",
            "DELETE FROM analytics.error_objects WHERE project_id = @p",
        })
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("p", projectId);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    [SkippableFact]
    public async Task WriteErrorObjects_then_QueryErrorGroupIds_returns_grouped_counts()
    {
        Skip.IfNot(_pgReachable, "Postgres + analytics schema not reachable");
        var pid = NextProjectId();
        try
        {
            var ts = DateTime.UtcNow;
            // 5 error_objects across 3 groups: group 100 has 3, group 200 has 1, group 300 has 1
            await _store.WriteErrorObjectsAsync(
            [
                new ErrorObjectRowInput { ProjectId = pid, ErrorObjectId = 1, ErrorGroupId = 100, Timestamp = ts },
                new ErrorObjectRowInput { ProjectId = pid, ErrorObjectId = 2, ErrorGroupId = 100, Timestamp = ts },
                new ErrorObjectRowInput { ProjectId = pid, ErrorObjectId = 3, ErrorGroupId = 100, Timestamp = ts },
                new ErrorObjectRowInput { ProjectId = pid, ErrorObjectId = 4, ErrorGroupId = 200, Timestamp = ts },
                new ErrorObjectRowInput { ProjectId = pid, ErrorObjectId = 5, ErrorGroupId = 300, Timestamp = ts },
            ]);

            var (ids, total) = await _store.QueryErrorGroupIdsAsync(
                pid,
                new QueryInput
                {
                    DateRange = new DateRangeRequiredInput
                    {
                        StartDate = ts.AddMinutes(-1),
                        EndDate = ts.AddMinutes(1),
                    },
                },
                count: 50, page: 0);

            Assert.Equal(3, total);
            Assert.Equal(3, ids.Count);
            Assert.Equal(100, ids[0]); // most-frequent first
        }
        finally
        {
            await CleanupAsync(pid);
        }
    }

    [SkippableFact]
    public async Task WriteErrorGroups_upsert_advances_updated_at()
    {
        // ErrorGroups uses UPSERT with WHERE updated_at <= EXCLUDED.updated_at
        // - re-inserting with a newer timestamp wins; an older timestamp is
        // discarded. This mirrors CH's ReplacingMergeTree(UpdatedAt) behavior.
        Skip.IfNot(_pgReachable, "Postgres + analytics schema not reachable");
        var pid = NextProjectId();
        try
        {
            var t1 = DateTime.UtcNow;
            await _store.WriteErrorGroupsAsync(
            [
                new ErrorGroupRowInput
                {
                    ProjectId = pid, ErrorGroupId = 42,
                    SecureId = "abc", CreatedAt = t1, UpdatedAt = t1,
                    Event = "TypeError: foo", Type = "TypeError", State = "OPEN",
                    ServiceName = "v1", Environments = "prod",
                },
            ]);

            // Newer insert wins
            var t2 = t1.AddMinutes(5);
            await _store.WriteErrorGroupsAsync(
            [
                new ErrorGroupRowInput
                {
                    ProjectId = pid, ErrorGroupId = 42,
                    SecureId = "abc", CreatedAt = t1, UpdatedAt = t2,
                    Event = "TypeError: foo", Type = "TypeError", State = "RESOLVED",
                    ServiceName = "v2", Environments = "prod",
                },
            ]);

            // Older insert ignored
            await _store.WriteErrorGroupsAsync(
            [
                new ErrorGroupRowInput
                {
                    ProjectId = pid, ErrorGroupId = 42,
                    SecureId = "abc", CreatedAt = t1, UpdatedAt = t1.AddSeconds(1),
                    Event = "stale", Type = "stale", State = "STALE",
                    ServiceName = "stale", Environments = "stale",
                },
            ]);

            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "SELECT state, service_name FROM analytics.error_groups WHERE project_id = @p AND error_group_id = 42",
                conn);
            cmd.Parameters.AddWithValue("p", pid);
            await using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();
            Assert.Equal("RESOLVED", reader.GetString(0));   // t2 won
            Assert.Equal("v2", reader.GetString(1));         // not "stale"
        }
        finally
        {
            await CleanupAsync(pid);
        }
    }

    [SkippableFact]
    public async Task GetErrorsKeysAsync_returns_reserved_list()
    {
        Skip.IfNot(_pgReachable, "Postgres + analytics schema not reachable");

        var keys = await _store.GetErrorsKeysAsync(1, default, default, null);
        Assert.Contains(keys, k => k.Name == "event");
        Assert.Contains(keys, k => k.Name == "url");
        Assert.Contains(keys, k => k.Name == "browser");

        var browserOnly = await _store.GetErrorsKeysAsync(1, default, default, "browser");
        Assert.All(browserOnly, k => Assert.Contains("browser", k.Name, StringComparison.OrdinalIgnoreCase));
    }

    [SkippableFact]
    public async Task GetErrorsKeyValuesAsync_returns_distinct_values()
    {
        Skip.IfNot(_pgReachable, "Postgres + analytics schema not reachable");
        var pid = NextProjectId();
        try
        {
            var ts = DateTime.UtcNow;
            await _store.WriteErrorObjectsAsync(
            [
                new ErrorObjectRowInput { ProjectId = pid, ErrorObjectId = 1, ErrorGroupId = 1, Timestamp = ts, Browser = "Chrome", OS = "macOS" },
                new ErrorObjectRowInput { ProjectId = pid, ErrorObjectId = 2, ErrorGroupId = 2, Timestamp = ts, Browser = "Firefox", OS = "Linux" },
                new ErrorObjectRowInput { ProjectId = pid, ErrorObjectId = 3, ErrorGroupId = 3, Timestamp = ts, Browser = "Chrome", OS = "Windows" },
            ]);

            var browsers = await _store.GetErrorsKeyValuesAsync(
                pid, "browser", ts.AddDays(-1), ts.AddDays(1), null, 50);
            Assert.Equal(2, browsers.Count);
            Assert.Contains("Chrome", browsers);
            Assert.Contains("Firefox", browsers);

            // Unknown key resolves to null in SanitizeColumnName, GetValues returns empty
            var unknown = await _store.GetErrorsKeyValuesAsync(
                pid, "totally_made_up_key", ts.AddDays(-1), ts.AddDays(1), null, 50);
            Assert.Empty(unknown);
        }
        finally
        {
            await CleanupAsync(pid);
        }
    }
}
