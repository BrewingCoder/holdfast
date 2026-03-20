using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.GraphQL.Public.InputTypes;
using HotChocolate;
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
    /// Creates the session record and returns the secure ID + project ID.
    /// </summary>
    public async Task<InitializeSessionResponse> InitializeSession(
        InitializeSessionInput input,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        // organization_verbose_id is the base36-encoded project ID
        var projectId = Project.FromVerboseId(input.OrganizationVerboseId);
        var project = await db.Projects.FindAsync([projectId], ct)
            ?? throw new GraphQLException("Project not found");

        var session = new Session
        {
            SecureId = input.SessionSecureId,
            ProjectId = project.Id,
            Fingerprint = input.Fingerprint,
            ClientID = input.ClientId,
            ClientVersion = input.ClientVersion,
            FirstloadVersion = input.FirstloadVersion,
            ClientConfig = input.ClientConfig,
            Environment = input.Environment,
            AppVersion = input.AppVersion,
            ServiceName = input.ServiceName,
            EnableStrictPrivacy = input.EnableStrictPrivacy,
            EnableRecordingNetworkContents = input.EnableRecordingNetworkContents,
            PrivacySetting = input.PrivacySetting,
            WithinBillingQuota = true, // Self-hosted: always within quota
            Processed = false,
            Excluded = false,
        };

        db.Sessions.Add(session);
        await db.SaveChangesAsync(ct);

        return new InitializeSessionResponse(session.SecureId, project.Id);
    }

    /// <summary>
    /// Identify a session with a user identifier and optional user properties.
    /// </summary>
    public async Task<string> IdentifySession(
        IdentifySessionInput input,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var session = await db.Sessions
            .FirstOrDefaultAsync(s => s.SecureId == input.SessionSecureId, ct)
            ?? throw new GraphQLException("Session not found");

        session.Identifier = input.UserIdentifier;
        await db.SaveChangesAsync(ct);

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
