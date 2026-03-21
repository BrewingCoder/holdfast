namespace HoldFast.GraphQL.Public.InputTypes;

/// <summary>
/// A key-value tag attached to a metric data point (e.g., tag="service", value="api").
/// </summary>
public record MetricTag(
    string Name,
    string Value);

/// <summary>
/// Input for metric ingestion. Links a named numeric value to a session, span, and trace
/// with optional categorization and tags.
/// </summary>
public record MetricInput(
    string SessionSecureId,
    string? SpanId,
    string? ParentSpanId,
    string? TraceId,
    string? Group,
    string Name,
    double Value,
    string? Category,
    DateTime Timestamp,
    List<MetricTag>? Tags);
