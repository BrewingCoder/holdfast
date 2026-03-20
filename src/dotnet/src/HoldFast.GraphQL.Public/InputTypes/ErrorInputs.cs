namespace HoldFast.GraphQL.Public.InputTypes;

public record StackFrameInput(
    string? FunctionName,
    string? FileName,
    int? LineNumber,
    int? ColumnNumber,
    bool? IsEval,
    bool? IsNative,
    string? Source);

public record ErrorObjectInput(
    string Event,
    string Type,
    string Url,
    string Source,
    int LineNumber,
    int ColumnNumber,
    List<StackFrameInput> StackTrace,
    DateTime Timestamp,
    string? Payload);

public record ServiceInput(
    string Name,
    string Version);

public record BackendErrorObjectInput(
    string? SessionSecureId,
    string? RequestId,
    string? TraceId,
    string? SpanId,
    string? LogCursor,
    string Event,
    string Type,
    string Url,
    string Source,
    string StackTrace,
    DateTime Timestamp,
    string? Payload,
    ServiceInput Service,
    string Environment);
