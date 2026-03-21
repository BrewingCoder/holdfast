using HoldFast.Domain.Enums;

namespace HoldFast.Data.ClickHouse.Models;

/// <summary>
/// Represents a row in the ClickHouse logs table.
/// Read-only model — logs are written via Kafka consumers, read via ClickHouse queries.
/// </summary>
public class LogRow
{
    public DateTime Timestamp { get; set; }
    public int ProjectId { get; set; }
    public string TraceId { get; set; } = string.Empty;
    public string SpanId { get; set; } = string.Empty;
    public string SecureSessionId { get; set; } = string.Empty;
    public string UUID { get; set; } = string.Empty;
    public uint TraceFlags { get; set; }
    public string SeverityText { get; set; } = string.Empty;
    public int SeverityNumber { get; set; }
    public LogSource Source { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string ServiceVersion { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public Dictionary<string, string> LogAttributes { get; set; } = new();
    public string Environment { get; set; } = string.Empty;

    /// <summary>
    /// Generate a cursor string for pagination (matching Go's encodeCursor).
    /// Format: base64("{RFC3339},{uuid}")
    /// </summary>
    public string Cursor => CursorHelper.Encode(Timestamp, UUID);
}

/// <summary>
/// Paginated log result with cursor-based navigation.
/// </summary>
public class LogConnection
{
    public List<LogEdge> Edges { get; set; } = [];
    public PageInfo PageInfo { get; set; } = new();
}

/// <summary>
/// A single log entry in a paginated result, paired with its cursor.
/// </summary>
public class LogEdge
{
    public LogRow Node { get; set; } = null!;
    public string Cursor { get; set; } = string.Empty;
}

/// <summary>
/// Relay-style pagination metadata for cursor-based queries.
/// </summary>
public class PageInfo
{
    public bool HasNextPage { get; set; }
    public bool HasPreviousPage { get; set; }
    public string? StartCursor { get; set; }
    public string? EndCursor { get; set; }
}
