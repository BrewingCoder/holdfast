using HoldFast.Analytics.Models;

namespace HoldFast.Analytics;

/// <summary>
/// Backend-neutral store for session analytics queries (sessions search,
/// histograms, key/value discovery for the dashboard sessions filter UI).
///
/// Note: actual session-replay payloads (events, snapshots) live in blob
/// storage, not in the analytics store. This interface only covers the
/// search/aggregation surface.
/// </summary>
public interface ISessionAnalyticsStore
{
    Task<List<HistogramBucket>> ReadSessionsHistogramAsync(
        int projectId,
        QueryInput query,
        CancellationToken ct = default);

    Task<(List<int> Ids, long Total)> QuerySessionIdsAsync(
        int projectId,
        QueryInput query,
        int count,
        int page,
        string? sortField = null,
        bool sortDesc = true,
        CancellationToken ct = default);

    Task<List<QueryKey>> GetSessionsKeysAsync(
        int projectId,
        DateTime startDate,
        DateTime endDate,
        string? query,
        CancellationToken ct = default);

    Task<List<string>> GetSessionsKeyValuesAsync(
        int projectId,
        string keyName,
        DateTime startDate,
        DateTime endDate,
        string? query,
        int? count,
        CancellationToken ct = default);

    Task WriteSessionsAsync(
        IEnumerable<SessionRowInput> sessions,
        CancellationToken ct = default);
}
