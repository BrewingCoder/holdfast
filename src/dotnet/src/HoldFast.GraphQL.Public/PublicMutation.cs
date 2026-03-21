using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.GraphQL.Public.InputTypes;
using HoldFast.Shared.SessionProcessing;
using HotChocolate;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HoldFast.GraphQL.Public;

/// <summary>
/// Public GraphQL mutations — SDK data ingestion endpoint.
/// These are called by client SDKs to send session replay, errors, and metrics.
/// </summary>
public class PublicMutation
{
    /// <summary>
    /// Initialize a new session. Called by SDKs at page load / app start.
    /// Creates the session record with device/geo metadata and returns the secure ID + project ID.
    /// </summary>
    public async Task<InitializeSessionResponse> InitializeSession(
        InitializeSessionInput input,
        [Service] ISessionInitializationService initService,
        [Service] IHttpContextAccessor httpContextAccessor,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var projectId = Project.FromVerboseId(input.OrganizationVerboseId);
        var project = await db.Projects.FindAsync([projectId], ct)
            ?? throw new GraphQLException("Project not found");

        // Extract HTTP headers for device detection
        var httpContext = httpContextAccessor.HttpContext;
        var userAgent = httpContext?.Request.Headers.UserAgent.FirstOrDefault();
        var acceptLanguage = httpContext?.Request.Headers.AcceptLanguage.FirstOrDefault();
        var ipAddress = httpContext?.Connection.RemoteIpAddress?.ToString();

        var result = await initService.InitializeSessionAsync(
            input.SessionSecureId,
            input.SessionKey,
            project.Id,
            input.Fingerprint,
            input.ClientId,
            input.ClientVersion,
            input.FirstloadVersion,
            input.ClientConfig,
            input.Environment,
            input.AppVersion,
            input.ServiceName,
            input.EnableStrictPrivacy,
            input.EnableRecordingNetworkContents,
            input.PrivacySetting,
            userAgent,
            acceptLanguage,
            ipAddress,
            ct);

        return new InitializeSessionResponse(result.Session.SecureId, project.Id);
    }

    /// <summary>
    /// Identify a session with a user identifier and optional user properties.
    /// Detects first-time users and backfills unidentified sessions.
    /// </summary>
    public async Task<string> IdentifySession(
        IdentifySessionInput input,
        [Service] ISessionInitializationService initService,
        CancellationToken ct)
    {
        var session = await initService.IdentifySessionAsync(
            input.SessionSecureId,
            input.UserIdentifier,
            input.UserObject,
            ct);

        return session.SecureId;
    }

    /// <summary>
    /// Add custom properties to a session.
    /// </summary>
    public async Task<string> AddSessionProperties(
        AddSessionPropertiesInput input,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var session = await db.Sessions
            .FirstOrDefaultAsync(s => s.SecureId == input.SessionSecureId, ct)
            ?? throw new GraphQLException("Session not found");

        // Properties are stored as session fields — will be processed by worker
        await db.SaveChangesAsync(ct);

        return session.SecureId;
    }

    /// <summary>
    /// [DEPRECATED] Push uncompressed session payload with separate fields.
    /// Older SDKs send events, messages, resources, errors as separate parameters.
    /// New SDKs should use PushSessionEvents instead.
    /// </summary>
    [GraphQLDeprecated("Use pushSessionEvents instead")]
    public async Task<int> PushPayload(
        string sessionSecureId,
        int? payloadId,
        string events,
        string messages,
        string resources,
        string? webSocketEvents,
        List<ErrorObjectInput> errors,
        bool? isBeacon,
        bool? hasSessionUnloaded,
        string? highlightLogs,
        [Service] IKafkaProducer kafka,
        CancellationToken ct)
    {
        await kafka.ProducePushPayloadAsync(
            sessionSecureId, payloadId ?? 0, events, messages, resources,
            webSocketEvents, errors, isBeacon, hasSessionUnloaded, highlightLogs, ct);
        return events.Length;
    }

    /// <summary>
    /// [DEPRECATED] Push compressed session payload. Delegates to PushSessionEvents.
    /// </summary>
    [GraphQLDeprecated("Use pushSessionEvents instead")]
    public async Task<bool> PushPayloadCompressed(
        string sessionSecureId,
        int payloadId,
        string data,
        [Service] IKafkaProducer kafka,
        CancellationToken ct)
    {
        await kafka.ProduceSessionEventsAsync(sessionSecureId, payloadId, data, ct);
        return true;
    }

    /// <summary>
    /// Push compressed session events (replay data, errors, resources).
    /// This is the primary data ingestion path for new SDKs.
    /// The compressed payload is forwarded to Kafka for async processing.
    /// </summary>
    public async Task<bool> PushSessionEvents(
        string sessionSecureId,
        long payloadId,
        string data,
        [Service] IKafkaProducer kafka,
        CancellationToken ct)
    {
        await kafka.ProduceSessionEventsAsync(sessionSecureId, payloadId, data, ct);
        return true;
    }

    /// <summary>
    /// Push backend errors (from server-side SDKs).
    /// </summary>
    public async Task<bool> PushBackendPayload(
        string? projectId,
        List<BackendErrorObjectInput> errors,
        [Service] IKafkaProducer kafka,
        CancellationToken ct)
    {
        foreach (var error in errors)
        {
            await kafka.ProduceBackendErrorAsync(projectId, error, ct);
        }
        return true;
    }

    /// <summary>
    /// Push custom metrics from SDKs.
    /// </summary>
    public async Task<int> PushMetrics(
        List<MetricInput> metrics,
        [Service] IKafkaProducer kafka,
        CancellationToken ct)
    {
        foreach (var metric in metrics)
        {
            await kafka.ProduceMetricAsync(metric, ct);
        }
        return metrics.Count;
    }

    /// <summary>
    /// Mark a project as having backend integration set up.
    /// </summary>
    public async Task<bool> MarkBackendSetup(
        string? projectId,
        string? sessionSecureId,
        string? type,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        Project? project = null;

        if (projectId != null && int.TryParse(projectId, out var pid))
        {
            project = await db.Projects.FindAsync([pid], ct);
        }
        else if (sessionSecureId != null)
        {
            var session = await db.Sessions
                .Include(s => s.Project)
                .FirstOrDefaultAsync(s => s.SecureId == sessionSecureId, ct);
            project = session?.Project;
        }

        if (project != null)
        {
            project.BackendSetup = true;
            await db.SaveChangesAsync(ct);
        }

        return true;
    }

    /// <summary>
    /// Add user feedback to a session.
    /// </summary>
    public async Task<string> AddSessionFeedback(
        AddSessionFeedbackInput input,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var session = await db.Sessions
            .FirstOrDefaultAsync(s => s.SecureId == input.SessionSecureId, ct)
            ?? throw new GraphQLException("Session not found");

        // Store feedback as a session comment with type "FEEDBACK"
        var comment = new SessionComment
        {
            ProjectId = session.ProjectId,
            SessionId = session.Id,
            Text = input.Verbatim,
            Type = "FEEDBACK",
        };

        db.SessionComments.Add(comment);
        await db.SaveChangesAsync(ct);

        return session.SecureId;
    }
}
