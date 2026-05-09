using HoldFast.Analytics.Models;
using HoldFast.Data.Postgres;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HoldFast.Data.Tests.Postgres;

/// <summary>
/// HOL-33: tests for the two stub stores (events + alert state).
///
/// Both intentionally stub their methods to match the CH gap (events queries
/// a removed Go-side `fields` table; alert state machine isn't ported to
/// .NET yet). These tests pin the stub behavior so a future "real impl" PR
/// has to acknowledge it's changing.
/// </summary>
public class PostgresEventAndAlertStoreTests
{
    private static readonly IConfiguration EmptyConfig =
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgreSQL"] =
                    "Host=does-not-matter-tests-do-not-connect;Database=x;Username=x;Password=x",
            })
            .Build();

    private static readonly Microsoft.Extensions.Options.IOptions<PostgresAnalyticsOptions> Opts =
        Options.Create(new PostgresAnalyticsOptions());

    // ── EventFieldStore stubs ────────────────────────────────────────

    [Fact]
    public async Task GetEventsKeysAsync_returns_reserved_list()
    {
        var store = new PostgresEventFieldStore(Opts, EmptyConfig,
            NullLogger<PostgresEventFieldStore>.Instance);

        var keys = await store.GetEventsKeysAsync(1, default, default, null, null);
        Assert.Contains(keys, k => k.Name == "event");
        Assert.Contains(keys, k => k.Name == "timestamp");
        Assert.Contains(keys, k => k.Name == "session_id");
    }

    [Fact]
    public async Task GetEventsKeysAsync_filters_by_query_substring()
    {
        var store = new PostgresEventFieldStore(Opts, EmptyConfig,
            NullLogger<PostgresEventFieldStore>.Instance);

        var keys = await store.GetEventsKeysAsync(1, default, default, "session", null);
        Assert.All(keys, k => Assert.Contains("session", k.Name, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetEventsKeyValuesAsync_returns_empty_stub()
    {
        // Locked behavior: stub returns empty list. When the .NET worker starts
        // populating an events catalog, this test gets updated.
        var store = new PostgresEventFieldStore(Opts, EmptyConfig,
            NullLogger<PostgresEventFieldStore>.Instance);

        var values = await store.GetEventsKeyValuesAsync(1, "any_key", default, default, null, null, null);
        Assert.Empty(values);
    }

    // ── AlertStateStore stubs ────────────────────────────────────────

    [Fact]
    public async Task GetLastAlertStateChangesAsync_returns_empty_stub()
    {
        var store = new PostgresAlertStateStore(Opts, EmptyConfig,
            NullLogger<PostgresAlertStateStore>.Instance);

        var rows = await store.GetLastAlertStateChangesAsync(1, 1, default, default);
        Assert.Empty(rows);
    }

    [Fact]
    public async Task GetAlertingAlertStateChangesAsync_returns_empty_stub()
    {
        var store = new PostgresAlertStateStore(Opts, EmptyConfig,
            NullLogger<PostgresAlertStateStore>.Instance);

        var rows = await store.GetAlertingAlertStateChangesAsync(1, 1, default, default);
        Assert.Empty(rows);
    }

    [Fact]
    public async Task GetLastAlertingStatesAsync_returns_empty_stub()
    {
        var store = new PostgresAlertStateStore(Opts, EmptyConfig,
            NullLogger<PostgresAlertStateStore>.Instance);

        var rows = await store.GetLastAlertingStatesAsync(1, 1, default, default);
        Assert.Empty(rows);
    }

    [Fact]
    public async Task WriteAlertStateChangesAsync_completes_without_throwing()
    {
        // Stub is a no-op — any input (including null/empty/unrelated) shouldn't throw.
        var store = new PostgresAlertStateStore(Opts, EmptyConfig,
            NullLogger<PostgresAlertStateStore>.Instance);

        await store.WriteAlertStateChangesAsync(1, []);
        await store.WriteAlertStateChangesAsync(1,
            [new AlertStateChangeRow { ProjectId = 1, AlertId = 1, State = "Firing" }]);
        // No assertion needed — reaching here means the stub didn't throw.
    }
}
