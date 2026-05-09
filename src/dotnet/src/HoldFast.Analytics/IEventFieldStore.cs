using HoldFast.Analytics.Models;

namespace HoldFast.Analytics;

/// <summary>
/// Backend-neutral store for the events-key/value discovery surface
/// (used by the dashboard's event-search autocomplete on top of session events).
/// </summary>
public interface IEventFieldStore
{
    Task<List<QueryKey>> GetEventsKeysAsync(
        int projectId,
        DateTime startDate,
        DateTime endDate,
        string? query,
        string? eventName,
        CancellationToken ct = default);

    Task<List<string>> GetEventsKeyValuesAsync(
        int projectId,
        string keyName,
        DateTime startDate,
        DateTime endDate,
        string? query,
        int? count,
        string? eventName,
        CancellationToken ct = default);
}
