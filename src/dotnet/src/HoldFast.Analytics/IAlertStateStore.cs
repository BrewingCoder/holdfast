using HoldFast.Analytics.Models;

namespace HoldFast.Analytics;

/// <summary>
/// Backend-neutral store for alert state-change history. Alert evaluation
/// reads recent state changes to detect transitions and avoid duplicate
/// notifications; the worker writes new state changes after evaluation.
/// </summary>
public interface IAlertStateStore
{
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
}
