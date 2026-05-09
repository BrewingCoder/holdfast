using HoldFast.Analytics;
using HoldFast.Analytics.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HoldFast.Data.Postgres;

/// <summary>
/// Postgres implementation of <see cref="IAlertStateStore"/>.
///
/// HOL-33. The CH impl currently stubs all four methods (returns empty lists
/// / Task.CompletedTask) — the alert state machine isn't fully ported to .NET
/// yet (it lived in Go's worker code). PG matches that to keep behavior
/// identical across backends. When the .NET alert evaluator is built, both
/// PostgresAlertStateStore and ClickHouseService get real implementations
/// against analytics.alert_state_changes (CH already has that table from
/// migration 000105; PG can adopt the same schema).
/// </summary>
public sealed class PostgresAlertStateStore : IAlertStateStore
{
    private readonly PostgresAnalyticsOptions _options;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PostgresAlertStateStore> _logger;

    public PostgresAlertStateStore(
        IOptions<PostgresAnalyticsOptions> options,
        IConfiguration configuration,
        ILogger<PostgresAlertStateStore> logger)
    {
        _options = options.Value;
        _configuration = configuration;
        _logger = logger;
    }

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
}
