namespace HoldFast.Data.ClickHouse.Models;

/// <summary>
/// A time-bucketed aggregation result from ClickHouse metrics queries.
/// </summary>
public class MetricsBucket
{
    public DateTime BucketStart { get; set; }
    public DateTime BucketEnd { get; set; }
    public string? Group { get; set; }
    public double Value { get; set; }
    public double? MetricValue { get; set; }
    public long Count { get; set; }
}

/// <summary>
/// Result of a metrics query — a list of time-series buckets.
/// </summary>
public class MetricsBuckets
{
    public List<MetricsBucket> Buckets { get; set; } = [];
    public long TotalCount { get; set; }
    public double? SampleFactor { get; set; }
}

/// <summary>
/// Histogram bucket for distribution queries.
/// </summary>
public class HistogramBucket
{
    public DateTime BucketStart { get; set; }
    public DateTime BucketEnd { get; set; }
    public long Count { get; set; }
    public string? Group { get; set; }
}

/// <summary>
/// A searchable key returned by key-discovery queries (sessions_keys, events_keys, etc.).
/// </summary>
public class QueryKey
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "String";
}

/// <summary>
/// Common query input matching Go's QueryInput.
/// </summary>
public class QueryInput
{
    public string Query { get; set; } = string.Empty;
    public DateTime DateRangeStart { get; set; }
    public DateTime DateRangeEnd { get; set; }
}

/// <summary>
/// Pagination parameters for cursor-based ClickHouse queries.
/// </summary>
public class ClickHousePagination
{
    public string? After { get; set; }
    public string? Before { get; set; }
    public string? At { get; set; }
    public string Direction { get; set; } = "DESC";
    public int Limit { get; set; } = 50;
}
