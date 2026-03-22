namespace HoldFast.GraphQL.Public.InputTypes;

/// <summary>
/// Input for log ingestion via GraphQL or OTeL endpoint.
/// </summary>
public record LogInput(
    int ProjectId,
    DateTime Timestamp,
    string TraceId,
    string SpanId,
    string SecureSessionId,
    string SeverityText,
    int SeverityNumber,
    string Source,
    string ServiceName,
    string ServiceVersion,
    string Body,
    Dictionary<string, string>? LogAttributes,
    string Environment);

/// <summary>
/// Input for trace span ingestion via GraphQL or OTeL endpoint.
/// </summary>
public record TraceInput(
    int ProjectId,
    DateTime Timestamp,
    string TraceId,
    string SpanId,
    string ParentSpanId,
    string SecureSessionId,
    string ServiceName,
    string ServiceVersion,
    string Environment,
    string SpanName,
    string SpanKind,
    long Duration,
    string StatusCode,
    string StatusMessage,
    Dictionary<string, string>? TraceAttributes,
    bool HasErrors);
