using HoldFast.Domain.Entities;

namespace HoldFast.GraphQL.Private;

/// <summary>
/// Status of a specific integration setup for a project.
/// </summary>
public record IntegrationStatus(
    bool Integrated,
    string ResourceType,
    DateTime? CreatedAt = null);

/// <summary>
/// A timeline indicator event within a session replay.
/// </summary>
public record TimelineIndicatorEvent(
    string SessionSecureId,
    double Timestamp,
    double Sid,
    object? Data,
    int Type);

/// <summary>
/// Aggregated session report per user.
/// </summary>
public record SessionsReportRow(
    string Key,
    string Email,
    long NumSessions,
    DateTime FirstSession,
    DateTime LastSession,
    long NumDaysVisited,
    long NumMonthsVisited,
    double AvgActiveLengthMins,
    double MaxActiveLengthMins,
    double TotalActiveLengthMins,
    double AvgLengthMins,
    double MaxLengthMins,
    double TotalLengthMins,
    string Location);

/// <summary>
/// An error tag matched against a query string with relevance score.
/// </summary>
public record MatchedErrorTag(
    int Id,
    string Title,
    string Description,
    double Score);

/// <summary>
/// Input for updating integration project mappings.
/// </summary>
public record IntegrationProjectMappingInput(
    int ProjectId,
    string? ExternalId);

/// <summary>
/// Session replay payload: events, errors, rage clicks, and comments.
/// </summary>
public record SessionPayload(
    string Events,
    List<ErrorObject> Errors,
    List<RageClickEvent> RageClicks,
    List<SessionComment> SessionComments,
    string LastUserInteractionTime);

/// <summary>
/// Paginated error group instances result.
/// </summary>
public record ErrorGroupInstances(
    List<ErrorObject> ErrorObjects,
    long TotalCount);

/// <summary>
/// Referrer host with visit count and percentage.
/// </summary>
public record ReferrerTablePayload(
    string Host,
    int Count,
    double Percent);

/// <summary>
/// Top user by active time.
/// </summary>
public record TopUsersPayload(
    int Id,
    string Identifier,
    int TotalActiveTime,
    double ActiveTimePercentage,
    string UserProperties);

/// <summary>
/// Average session length result.
/// </summary>
public record AverageSessionLength(double Length);

/// <summary>
/// New users count result.
/// </summary>
public record NewUsersCount(long Count);

/// <summary>
/// Unique fingerprint count result.
/// </summary>
public record UserFingerprintCount(long Count);

/// <summary>
/// Error group tag aggregation bucket.
/// </summary>
public record ErrorGroupTagAggregationBucket(
    string Key,
    long DocCount,
    double Percent);

/// <summary>
/// Error group tag aggregation with buckets.
/// </summary>
public record ErrorGroupTagAggregation(
    string Key,
    List<ErrorGroupTagAggregationBucket> Buckets);

/// <summary>
/// Sanitized Slack channel info.
/// </summary>
public record SanitizedSlackChannel(
    string? WebhookChannel,
    string? WebhookChannelId);

/// <summary>
/// Discord channel info.
/// </summary>
public record DiscordChannelInfo(
    string Id,
    string Name);

/// <summary>
/// Microsoft Teams channel info.
/// </summary>
public record MicrosoftTeamsChannelInfo(
    string Id,
    string Name);

/// <summary>
/// SSO login configuration.
/// </summary>
public record SSOLogin(
    string Domain,
    string ClientId);

/// <summary>
/// Input for alert destination configuration.
/// </summary>
public record AlertDestinationInput(
    string DestinationType,
    string? TypeId,
    string? TypeName);
