using HoldFast.Analytics;
using HoldFast.Analytics.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HoldFast.Data.Postgres;

/// <summary>
/// Postgres implementation of <see cref="IEventFieldStore"/>.
///
/// HOL-33. The CH impl queries a `fields` table populated by Go-side worker
/// code that's been removed in the .NET migration (same gap as documented
/// in PostgresSessionAnalyticsStore). Until the .NET worker rewires field
/// population, GetEventsKeys returns the hardcoded reserved list (matches
/// CH) and GetEventsKeyValues returns empty. Operators get an empty
/// autocomplete rather than missing values mixed with present ones.
/// </summary>
public sealed class PostgresEventFieldStore : IEventFieldStore
{
    private readonly PostgresAnalyticsOptions _options;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PostgresEventFieldStore> _logger;

    private static readonly string[] ReservedKeys =
        { "event", "timestamp", "session_id" };

    public PostgresEventFieldStore(
        IOptions<PostgresAnalyticsOptions> options,
        IConfiguration configuration,
        ILogger<PostgresEventFieldStore> logger)
    {
        _options = options.Value;
        _configuration = configuration;
        _logger = logger;
    }

    public Task<List<QueryKey>> GetEventsKeysAsync(
        int projectId, DateTime startDate, DateTime endDate,
        string? query, string? eventName, CancellationToken ct = default)
    {
        var keys = ReservedKeys
            .Where(k => string.IsNullOrEmpty(query)
                     || k.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(k => new QueryKey { Name = k, Type = "String" })
            .ToList();
        return Task.FromResult(keys);
    }

    public Task<List<string>> GetEventsKeyValuesAsync(
        int projectId, string keyName, DateTime startDate, DateTime endDate,
        string? query, int? count, string? eventName, CancellationToken ct = default)
    {
        // Stub matching the CH gap. When the .NET worker starts populating an
        // events catalog, this method gets the same query shape as
        // GetSessionsKeyValues — DISTINCT value FROM analytics.event_fields
        // WHERE key = ... AND day in range.
        return Task.FromResult(new List<string>());
    }
}
