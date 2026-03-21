namespace HoldFast.GraphQL.Public.InputTypes;

/// <summary>
/// A single frame in an error stack trace. Maps to one line in a JS/TS stack trace.
/// IsEval/IsNative indicate the execution context.
/// </summary>
public record StackFrameInput(
    string? FunctionName,
    string? FileName,
    int? LineNumber,
    int? ColumnNumber,
    bool? IsEval,
    bool? IsNative,
    string? Source);

/// <summary>
/// Frontend error object submitted by the browser SDK. Contains the error event message,
/// type, source URL, line/column numbers, full stack trace, and optional JSON payload.
/// </summary>
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

/// <summary>
/// Identifies the backend service that reported an error (name + version).
/// </summary>
public record ServiceInput(
    string Name,
    string Version);

/// <summary>
/// Backend error submitted by a server-side SDK. Includes distributed tracing context
/// (TraceId, SpanId), service identity, and the error's environment.
/// </summary>
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
