using HoldFast.Analytics.Models;

namespace HoldFast.Analytics;

/// <summary>
/// Backend-neutral store for error analytics (error groups + error objects
/// search, histograms, key/value discovery for the dashboard errors filter
/// UI, and worker-side ingest writes).
/// </summary>
public interface IErrorAnalyticsStore
{
    Task<(List<int> Ids, long Total)> QueryErrorGroupIdsAsync(
        int projectId,
        QueryInput query,
        int count,
        int page,
        CancellationToken ct = default);

    Task<List<HistogramBucket>> ReadErrorObjectsHistogramAsync(
        int projectId,
        QueryInput query,
        CancellationToken ct = default);

    Task<List<QueryKey>> GetErrorsKeysAsync(
        int projectId,
        DateTime startDate,
        DateTime endDate,
        string? query,
        CancellationToken ct = default);

    Task<List<string>> GetErrorsKeyValuesAsync(
        int projectId,
        string keyName,
        DateTime startDate,
        DateTime endDate,
        string? query,
        int? count,
        CancellationToken ct = default);

    Task WriteErrorGroupsAsync(
        IEnumerable<ErrorGroupRowInput> errorGroups,
        CancellationToken ct = default);

    Task WriteErrorObjectsAsync(
        IEnumerable<ErrorObjectRowInput> errorObjects,
        CancellationToken ct = default);
}
