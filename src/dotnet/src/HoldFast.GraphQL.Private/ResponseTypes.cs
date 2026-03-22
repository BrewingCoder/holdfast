using HoldFast.Domain.Entities;
using HotChocolate;

namespace HoldFast.GraphQL.Private;

/// <summary>
/// Workspace admin role with the admin entity and project scope.
/// Matches Go schema WorkspaceAdminRole type.
/// Fields workspaceId and projectIds are camelCase in the Go schema
/// so they need explicit [GraphQLName] to override snake_case convention.
/// </summary>
public record WorkspaceAdminRole(
    [property: GraphQLName("workspaceId")] string WorkspaceId,
    Admin Admin,
    string Role,
    [property: GraphQLName("projectIds")] List<string> ProjectIds);

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

/// <summary>
/// Combined projects and workspaces result.
/// </summary>
public record ProjectsAndWorkspacesResult(
    List<Project> Projects,
    List<Workspace> Workspaces);

/// <summary>
/// Result for project-or-workspace lookup.
/// </summary>
public record ProjectOrWorkspaceResult(
    Project? Project,
    Workspace? Workspace);

/// <summary>
/// All alert types for a project (alerts page).
/// </summary>
public record AlertsPagePayload(
    List<Alert> Alerts,
    List<ErrorAlert> ErrorAlerts,
    List<SessionAlert> SessionAlerts,
    List<LogAlert> LogAlerts,
    List<MetricMonitor> MetricMonitors);

/// <summary>
/// Log alerts for a project.
/// </summary>
public record LogAlertsPagePayload(
    List<LogAlert> LogAlerts);

/// <summary>
/// Session search results with totals.
/// </summary>
public record SessionResults(
    List<Session> Sessions,
    long TotalCount,
    long TotalLength,
    long TotalActiveLength);

/// <summary>
/// A single value suggestion with count and rank.
/// </summary>
public record ValueSuggestion(
    string Value,
    long Count,
    long Rank);

/// <summary>
/// Key-value suggestion pairing a key with its top values.
/// </summary>
public record KeyValueSuggestion(
    string Key,
    List<ValueSuggestion> Values);

/// <summary>
/// A structured log line.
/// </summary>
public record LogLine(
    DateTime Timestamp,
    string Body,
    string? Severity,
    string Labels);

/// <summary>
/// Search result from external issue tracker (Linear, Jira, GitHub, etc.).
/// </summary>
public record IssuesSearchResult(
    string Id,
    string Title,
    string IssueUrl);

/// <summary>
/// Input for mapping a Vercel project to a HoldFast project.
/// </summary>
public record VercelProjectMappingInput(
    string VercelProjectId,
    string? VercelProjectName,
    int? ProjectId);

/// <summary>
/// Input for mapping a ClickUp space/list to a HoldFast project.
/// </summary>
public record ClickUpProjectMappingInput(
    int ProjectId,
    string ClickUpSpaceId);

/// <summary>
/// Sessions histogram result — arrays of counts per time bucket.
/// Matches the Go schema SessionsHistogram type.
/// </summary>
public record SessionsHistogram(
    [property: GraphQLName("bucket_times")] List<DateTime> BucketTimes,
    [property: GraphQLName("sessions_without_errors")] List<long> SessionsWithoutErrors,
    [property: GraphQLName("sessions_with_errors")] List<long> SessionsWithErrors,
    [property: GraphQLName("total_sessions")] List<long> TotalSessions,
    [property: GraphQLName("inactive_lengths")] List<long> InactiveLengths,
    [property: GraphQLName("active_lengths")] List<long> ActiveLengths);

/// <summary>
/// Date range input for histogram bounds. Matches Go schema DateRangeInput.
/// </summary>
public class DateRangeInput
{
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}

/// <summary>
/// Options for date histogram queries. Matches Go schema DateHistogramOptions.
/// </summary>
public class DateHistogramOptions
{
    public DateRangeInput? Bounds { get; set; }
    public string? BucketSize { get; set; }
    public string? TimeZone { get; set; }
}

/// <summary>
/// Stub billing plan — returns unlimited defaults for self-hosted deployments.
/// </summary>
public record BillingPlan(
    string Type,
    string Interval,
    int MembersLimit,
    long SessionsLimit,
    long ErrorsLimit,
    long LogsLimit,
    long TracesLimit,
    long MetricsLimit,
    long SessionsRate,
    long ErrorsRate,
    long LogsRate,
    long TracesRate,
    long MetricsRate,
    [property: GraphQLName("aws_mp_subscription")] object? AwsMpSubscription);

/// <summary>
/// Stub billing details — returns zero meters and unlimited plan for self-hosted deployments.
/// All billing was removed from HoldFast; this stub satisfies the frontend contract.
/// </summary>
public record BillingDetails(
    BillingPlan Plan,
    long Meter,
    [property: GraphQLName("membersMeter")] long MembersMeter,
    [property: GraphQLName("errorsMeter")] long ErrorsMeter,
    [property: GraphQLName("logsMeter")] long LogsMeter,
    [property: GraphQLName("tracesMeter")] long TracesMeter,
    [property: GraphQLName("metricsMeter")] long MetricsMeter,
    [property: GraphQLName("sessionsBillingLimit")] long SessionsBillingLimit,
    [property: GraphQLName("errorsBillingLimit")] long ErrorsBillingLimit,
    [property: GraphQLName("logsBillingLimit")] long LogsBillingLimit,
    [property: GraphQLName("tracesBillingLimit")] long TracesBillingLimit,
    [property: GraphQLName("metricsBillingLimit")] long MetricsBillingLimit);

/// <summary>
/// Input for the updateAdminAboutYouDetails mutation — matches the Go schema AdminAboutYouDetails input.
/// Named to exactly match the Go schema type so HC emits "AdminAboutYouDetails" in the schema.
/// </summary>
public record AdminAboutYouDetails(
    string FirstName,
    string LastName,
    string UserDefinedRole,
    string UserDefinedPersona,
    string UserDefinedTeamSize,
    string HeardAbout,
    string Referral,
    string? Phone);
