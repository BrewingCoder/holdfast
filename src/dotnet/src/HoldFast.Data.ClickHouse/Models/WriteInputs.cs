namespace HoldFast.Data.ClickHouse.Models;

/// <summary>
/// Input for writing a log row to ClickHouse.
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
/// Input for writing a trace span row to ClickHouse.
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
/// Input for writing a session row to ClickHouse.
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
/// Input for writing an error group row to ClickHouse.
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
/// Input for writing an error object row to ClickHouse.
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
