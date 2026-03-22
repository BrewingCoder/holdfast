namespace HoldFast.GraphQL.Private;

/// <summary>
/// Result of a ClickHouse error group search.
/// </summary>
public class ErrorGroupSearchResult
{
    public List<int> ErrorGroupIds { get; set; } = [];
    public long TotalCount { get; set; }
}

/// <summary>
/// Result of a ClickHouse session search.
/// </summary>
public class SessionSearchResult
{
    public List<int> SessionIds { get; set; } = [];
    public long TotalCount { get; set; }
}
