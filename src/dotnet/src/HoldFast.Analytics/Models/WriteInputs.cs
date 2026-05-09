namespace HoldFast.Analytics.Models;

/// <summary>
/// Input for writing a log row.
/// Matches the log_rows table schema.
/// </summary>
public class LogRowInput
{
    public int ProjectId { get; set; }
    public DateTime Timestamp { get; set; }
    public string TraceId { get; set; } = string.Empty;
    public string SpanId { get; set; } = string.Empty;
    public string SecureSessionId { get; set; } = string.Empty;
    public string SeverityText { get; set; } = string.Empty;
    public int SeverityNumber { get; set; }
    public string Source { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string ServiceVersion { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public Dictionary<string, string> LogAttributes { get; set; } = new();
    public string Environment { get; set; } = string.Empty;
}

/// <summary>
/// Input for writing a trace span row.
/// Matches the trace_rows table schema.
/// </summary>
public class TraceRowInput
{
    public int ProjectId { get; set; }
    public DateTime Timestamp { get; set; }
    public string TraceId { get; set; } = string.Empty;
    public string SpanId { get; set; } = string.Empty;
    public string ParentSpanId { get; set; } = string.Empty;
    public string SecureSessionId { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string ServiceVersion { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string SpanName { get; set; } = string.Empty;
    public string SpanKind { get; set; } = string.Empty;
    public long Duration { get; set; }
    public string StatusCode { get; set; } = string.Empty;
    public string StatusMessage { get; set; } = string.Empty;
    public Dictionary<string, string> TraceAttributes { get; set; } = new();
    public bool HasErrors { get; set; }
}

/// <summary>
/// Input for writing a session row to the analytics store.
/// Matches the sessions table schema used for analytics/search.
/// </summary>
public class SessionRowInput
{
    public int ProjectId { get; set; }
    public int SessionId { get; set; }
    public string SecureSessionId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? Identifier { get; set; }
    public string? OSName { get; set; }
    public string? OSVersion { get; set; }
    public string? BrowserName { get; set; }
    public string? BrowserVersion { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? Environment { get; set; }
    public string? AppVersion { get; set; }
    public string? ServiceName { get; set; }
    public int ActiveLength { get; set; }
    public int Length { get; set; }
    public int PagesVisited { get; set; }
    public bool HasErrors { get; set; }
    public bool HasRageClicks { get; set; }
    public bool Processed { get; set; }
    public bool FirstTime { get; set; }
}

/// <summary>
/// Input for writing an error group row to the analytics store.
/// Matches the error_groups table schema used for analytics/search.
/// </summary>
public class ErrorGroupRowInput
{
    public int ProjectId { get; set; }
    public int ErrorGroupId { get; set; }
    public string SecureId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? Event { get; set; }
    public string? Type { get; set; }
    public string State { get; set; } = "OPEN";
    public string? ServiceName { get; set; }
    public string? Environments { get; set; }
}

/// <summary>
/// Input for writing an error object row to the analytics store.
/// Matches the error_objects table schema used for analytics/search.
/// </summary>
public class ErrorObjectRowInput
{
    public int ProjectId { get; set; }
    public int ErrorObjectId { get; set; }
    public int ErrorGroupId { get; set; }
    public DateTime Timestamp { get; set; }
    public string? Event { get; set; }
    public string? Type { get; set; }
    public string? Url { get; set; }
    public string? Environment { get; set; }
    public string? OS { get; set; }
    public string? Browser { get; set; }
    public string? ServiceName { get; set; }
    public string? ServiceVersion { get; set; }
}

/// <summary>
/// OTeL-shaped metric row. Replaces the (name, value, tags) triple with the
/// full metric envelope so the analytics store can land rows in the schemas
/// upstream Highlight migrated to (metrics_sum / metrics_histogram /
/// metrics_summary). Sum and Gauge share metrics_sum and discriminate on
/// <see cref="Kind"/>; histograms get the bucket fields populated.
/// </summary>
public class MetricRowInput
{
    public int ProjectId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string MetricName { get; set; } = string.Empty;
    public string MetricDescription { get; set; } = string.Empty;
    public string MetricUnit { get; set; } = string.Empty;
    public MetricKind Kind { get; set; } = MetricKind.Gauge;
    public DateTime StartTimestamp { get; set; }
    public DateTime Timestamp { get; set; }
    public Dictionary<string, string> Attributes { get; set; } = new();
    public string SecureSessionId { get; set; } = string.Empty;

    // Sum / Gauge
    public double Value { get; set; }

    // Sum only — defaults match OTeL's UNSPECIFIED / non-monotonic
    public int AggregationTemporality { get; set; }
    public bool IsMonotonic { get; set; }

    // Histogram — defaults safe to ignore for non-Histogram kinds
    public ulong Count { get; set; }
    public double Sum { get; set; }
    public List<ulong> BucketCounts { get; set; } = new();
    public List<double> ExplicitBounds { get; set; } = new();
    public double Min { get; set; }
    public double Max { get; set; }
}

/// <summary>
/// OTeL metric instrument kind. Numeric values match the
/// <c>metrics_sum.MetricType</c> Enum8 column on the ClickHouse side
/// so the cast at write time is a no-op.
/// </summary>
public enum MetricKind
{
    Empty = 0,
    Gauge = 1,
    Sum = 2,
    Histogram = 3,
    ExponentialHistogram = 4,
    Summary = 5,
}
