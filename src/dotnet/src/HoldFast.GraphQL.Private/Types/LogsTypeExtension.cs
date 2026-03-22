using HoldFast.Data.ClickHouse.Models;
using HotChocolate;
using HotChocolate.Types;

namespace HoldFast.GraphQL.Private.Types;

/// <summary>
/// HC type extensions for ClickHouse log/trace models.
/// The Go schema uses camelCase field names; HC's SnakeCaseNamingConventions produces
/// snake_case, so we add camelCase aliases here to match the frontend's expected field names.
/// </summary>

// ── LogRow ───────────────────────────────────────────────────────────────

[ExtendObjectType(typeof(LogRow))]
public class LogRowTypeExtension
{
    [GraphQLName("level")]
    public string Level([Parent] LogRow row) => row.SeverityText;

    [GraphQLName("message")]
    public string Message([Parent] LogRow row) => row.Body;

    [GraphQLName("logAttributes")]
    public Dictionary<string, string> LogAttributes([Parent] LogRow row) => row.LogAttributes;

    [GraphQLName("traceID")]
    public string TraceID([Parent] LogRow row) => row.TraceId;

    [GraphQLName("spanID")]
    public string SpanID([Parent] LogRow row) => row.SpanId;

    [GraphQLName("secureSessionID")]
    public string SecureSessionID([Parent] LogRow row) => row.SecureSessionId;

    [GraphQLName("serviceName")]
    public string ServiceName([Parent] LogRow row) => row.ServiceName;

    [GraphQLName("serviceVersion")]
    public string ServiceVersion([Parent] LogRow row) => row.ServiceVersion;

    [GraphQLName("projectID")]
    public int ProjectID([Parent] LogRow row) => row.ProjectId;
}

// ── LogConnection / LogEdge ──────────────────────────────────────────────

[ExtendObjectType(typeof(LogConnection))]
public class LogConnectionTypeExtension
{
    /// <summary>
    /// Expose pageInfo with camelCase name; HC's convention would produce page_info.
    /// </summary>
    [GraphQLName("pageInfo")]
    public PageInfo PageInfoCamel([Parent] LogConnection conn) => conn.PageInfo;
}

// ── TraceRow ─────────────────────────────────────────────────────────────

[ExtendObjectType(typeof(TraceRow))]
public class TraceRowTypeExtension
{
    [GraphQLName("traceID")]
    public string TraceID([Parent] TraceRow row) => row.TraceId;

    [GraphQLName("spanID")]
    public string SpanID([Parent] TraceRow row) => row.SpanId;

    [GraphQLName("parentSpanID")]
    public string ParentSpanID([Parent] TraceRow row) => row.ParentSpanId;

    [GraphQLName("projectID")]
    public int ProjectID([Parent] TraceRow row) => row.ProjectId;

    [GraphQLName("secureSessionID")]
    public string SecureSessionID([Parent] TraceRow row) => row.SecureSessionId;

    [GraphQLName("traceState")]
    public string TraceState([Parent] TraceRow row) => row.TraceState;

    [GraphQLName("spanName")]
    public string SpanName([Parent] TraceRow row) => row.SpanName;

    [GraphQLName("spanKind")]
    public string SpanKind([Parent] TraceRow row) => row.SpanKind.ToString();

    [GraphQLName("hasErrors")]
    public bool HasErrors([Parent] TraceRow row) => row.HasErrors;

    [GraphQLName("traceAttributes")]
    public Dictionary<string, string> TraceAttributes([Parent] TraceRow row) => row.TraceAttributes;

    [GraphQLName("statusCode")]
    public string StatusCode([Parent] TraceRow row) => row.StatusCode;

    [GraphQLName("statusMessage")]
    public string StatusMessage([Parent] TraceRow row) => row.StatusMessage;

    [GraphQLName("serviceName")]
    public string ServiceName([Parent] TraceRow row) => row.ServiceName;

    [GraphQLName("serviceVersion")]
    public string ServiceVersion([Parent] TraceRow row) => row.ServiceVersion;
}

// ── TraceConnection ──────────────────────────────────────────────────────

[ExtendObjectType(typeof(TraceConnection))]
public class TraceConnectionTypeExtension
{
    /// <summary>
    /// Expose pageInfo with camelCase name; HC's convention would produce page_info.
    /// </summary>
    [GraphQLName("pageInfo")]
    public PageInfo PageInfoCamel([Parent] TraceConnection conn) => conn.PageInfo;
}

// ── PageInfo ─────────────────────────────────────────────────────────────

[ExtendObjectType(typeof(PageInfo))]
public class PageInfoTypeExtension
{
    [GraphQLName("hasNextPage")]
    public bool HasNextPage([Parent] PageInfo pi) => pi.HasNextPage;

    [GraphQLName("hasPreviousPage")]
    public bool HasPreviousPage([Parent] PageInfo pi) => pi.HasPreviousPage;

    [GraphQLName("startCursor")]
    public string? StartCursor([Parent] PageInfo pi) => pi.StartCursor;

    [GraphQLName("endCursor")]
    public string? EndCursor([Parent] PageInfo pi) => pi.EndCursor;
}
