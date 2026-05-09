using System.Text.Json;
using HotChocolate;

namespace HoldFast.GraphQL.Public.InputTypes;

/// <summary>
/// A single frame in an error stack trace. Maps to one line in a JS/TS stack trace.
/// IsEval/IsNative indicate the execution context. `args` holds opaque
/// argument values (matches Go schema's `args: [Any]`).
///
/// Field names use [GraphQLName] to expose camelCase to clients — the Go schema
/// (and the SDKs generated against it) uses camelCase here even though the
/// global convention is snake_case. Mixed casing in the upstream contract.
/// </summary>
public record StackFrameInput(
    [property: GraphQLName("functionName")] string? FunctionName,
    List<JsonElement?>? Args,
    [property: GraphQLName("fileName")] string? FileName,
    [property: GraphQLName("lineNumber")] int? LineNumber,
    [property: GraphQLName("columnNumber")] int? ColumnNumber,
    [property: GraphQLName("isEval")] bool? IsEval,
    [property: GraphQLName("isNative")] bool? IsNative,
    string? Source);

/// <summary>
/// Frontend error object submitted by the browser SDK. Contains the error event message,
/// type, source URL, line/column numbers, full stack trace, and optional JSON payload.
///
/// Field names: lineNumber/columnNumber/stackTrace are camelCase in the Go schema;
/// override the snake_case naming convention to match.
/// </summary>
public record ErrorObjectInput(
    string Event,
    string Type,
    string Url,
    string Source,
    [property: GraphQLName("lineNumber")] int LineNumber,
    [property: GraphQLName("columnNumber")] int ColumnNumber,
    [property: GraphQLName("stackTrace")] List<StackFrameInput?> StackTrace,
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
/// (TraceId, SpanId), service identity, and the error's environment. The Go schema's
/// BackendErrorObjectInput uses mixed casing: most fields are snake_case, `stackTrace`
/// is camelCase.
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
    [property: GraphQLName("stackTrace")] string StackTrace,
    DateTime Timestamp,
    string? Payload,
    ServiceInput Service,
    string Environment);
