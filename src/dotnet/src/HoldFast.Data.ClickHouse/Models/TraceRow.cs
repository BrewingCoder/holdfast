using HoldFast.Domain.Enums;

namespace HoldFast.Data.ClickHouse.Models;

/// <summary>
/// Represents a row in the ClickHouse traces table.
/// Read-only model — traces are written via OTLP collector, read via ClickHouse queries.
/// </summary>
public class TraceRow
{
    public DateTime Timestamp { get; set; }
    public string UUID { get; set; } = string.Empty;
    public string TraceId { get; set; } = string.Empty;
    public string SpanId { get; set; } = string.Empty;
    public string ParentSpanId { get; set; } = string.Empty;
    public string TraceState { get; set; } = string.Empty;
    public string SpanName { get; set; } = string.Empty;
    public SpanKind SpanKind { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string ServiceVersion { get; set; } = string.Empty;
    public Dictionary<string, string> TraceAttributes { get; set; } = new();
    public long Duration { get; set; } // nanoseconds
    public string StatusCode { get; set; } = string.Empty;
    public string StatusMessage { get; set; } = string.Empty;
    public int ProjectId { get; set; }
    public string SecureSessionId { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public bool HasErrors { get; set; }
    public List<TraceEvent> Events { get; set; } = [];
    public List<TraceLink> Links { get; set; } = [];

    public string Cursor => CursorHelper.Encode(Timestamp, UUID);
}

public class TraceEvent
{
    public DateTime Timestamp { get; set; }
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, string> Attributes { get; set; } = new();
}

public class TraceLink
{
    public string TraceId { get; set; } = string.Empty;
    public string SpanId { get; set; } = string.Empty;
    public string TraceState { get; set; } = string.Empty;
    public Dictionary<string, string> Attributes { get; set; } = new();
}

/// <summary>
/// Paginated trace result with cursor-based navigation.
/// </summary>
public class TraceConnection
{
    public List<TraceEdge> Edges { get; set; } = [];
    public PageInfo PageInfo { get; set; } = new();
}

public class TraceEdge
{
    public TraceRow Node { get; set; } = null!;
    public string Cursor { get; set; } = string.Empty;
}
