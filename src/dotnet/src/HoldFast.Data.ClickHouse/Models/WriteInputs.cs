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
