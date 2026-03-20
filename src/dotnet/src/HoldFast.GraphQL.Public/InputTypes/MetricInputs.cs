namespace HoldFast.GraphQL.Public.InputTypes;

public record MetricTag(
    string Name,
    string Value);

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
