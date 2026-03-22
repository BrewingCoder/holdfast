using HoldFast.Data.ClickHouse.Models;
using HoldFast.Domain.Entities;
using HoldFast.Domain.Enums;
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
/// Go schema uses camelCase for these fields (resourceType, createdAt),
/// so override the snake_case convention with explicit [GraphQLName].
/// </summary>
public record IntegrationStatus(
    bool Integrated,
    [property: GraphQLName("resourceType")] string ResourceType,
    [property: GraphQLName("createdAt")] DateTime? CreatedAt = null);

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
/// Paginated error group list result — matches Go schema error_groups wrapper type.
/// </summary>
public record ErrorGroupResults(
    [property: GraphQLName("error_groups")] List<ErrorGroup> ErrorGroups,
    [property: GraphQLName("totalCount")] long TotalCount);

/// <summary>
/// Paginated error group instances result.
/// </summary>
public record ErrorGroupInstances(
    List<ErrorObject> ErrorObjects,
    long TotalCount);

/// <summary>
/// A single frame of a parsed (structured) stack trace.
/// Matches Go schema ErrorTrace type. Fields use camelCase per Go schema.
/// </summary>
public record ErrorTrace(
    [property: GraphQLName("fileName")] string? FileName,
    [property: GraphQLName("lineNumber")] int? LineNumber,
    [property: GraphQLName("columnNumber")] int? ColumnNumber,
    [property: GraphQLName("functionName")] string? FunctionName);

/// <summary>
/// Error occurrence count for a date bucket — used in error_metrics.
/// Matches Go schema ErrorDistributionItem type.
/// </summary>
public record ErrorDistributionItem(
    [property: GraphQLName("error_group_id")] int ErrorGroupId,
    DateTime Date,
    string Name,
    long Value);

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
/// Discord channel info. GraphQL name matches Go schema DiscordChannel type.
/// </summary>
[GraphQLName("DiscordChannel")]
public record DiscordChannelInfo(
    string Id,
    string Name);

/// <summary>
/// Microsoft Teams channel info. GraphQL name matches Go schema MicrosoftTeamsChannel type.
/// </summary>
[GraphQLName("MicrosoftTeamsChannel")]
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
/// Go schema uses camelCase for these fields (totalCount not total_count).
/// </summary>
public record SessionResults(
    List<Session> Sessions,
    [property: GraphQLName("totalCount")] long TotalCount,
    [property: GraphQLName("totalLength")] long TotalLength,
    [property: GraphQLName("totalActiveLength")] long TotalActiveLength);

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
/// Bucket size spec for histogram queries. Matches Go schema DateHistogramBucketSize.
/// HC snake_case maps CalendarInterval→calendar_interval, Multiple→multiple.
/// </summary>
public class DateHistogramBucketSize
{
    public string CalendarInterval { get; set; } = string.Empty;
    public int Multiple { get; set; }
}

/// <summary>
/// Options for date histogram queries. Matches Go schema DateHistogramOptions.
/// </summary>
public class DateHistogramOptions
{
    public DateRangeInput? Bounds { get; set; }
    public DateHistogramBucketSize? BucketSize { get; set; }
    public string? TimeZone { get; set; }
}

/// <summary>
/// AWS Marketplace subscription info — always null in HoldFast (no SaaS billing).
/// Stub type required so the frontend schema validates when it queries this field.
/// </summary>
public record AwsMpSubscription(
    [property: GraphQLName("customer_identifier")] string? CustomerIdentifier,
    [property: GraphQLName("customer_aws_account_id")] string? CustomerAwsAccountId,
    [property: GraphQLName("product_code")] string? ProductCode);

/// <summary>
/// Stub billing plan — returns unlimited defaults for self-hosted deployments.
/// Go schema uses camelCase for limit/rate fields.
/// </summary>
public record BillingPlan(
    string Type,
    string Interval,
    [property: GraphQLName("membersLimit")] int MembersLimit,
    [property: GraphQLName("sessionsLimit")] long SessionsLimit,
    [property: GraphQLName("errorsLimit")] long ErrorsLimit,
    [property: GraphQLName("logsLimit")] long LogsLimit,
    [property: GraphQLName("tracesLimit")] long TracesLimit,
    [property: GraphQLName("metricsLimit")] long MetricsLimit,
    [property: GraphQLName("sessionsRate")] long SessionsRate,
    [property: GraphQLName("errorsRate")] long ErrorsRate,
    [property: GraphQLName("logsRate")] long LogsRate,
    [property: GraphQLName("tracesRate")] long TracesRate,
    [property: GraphQLName("metricsRate")] long MetricsRate,
    [property: GraphQLName("aws_mp_subscription")] AwsMpSubscription? AwsMpSubscription,
    // Self-hosted: billing limits are always enabled (unlimited quotas).
    [property: GraphQLName("enableBillingLimits")] bool EnableBillingLimits = true);

/// <summary>
/// Subscription discount — always null in HoldFast (no SaaS billing).
/// Concrete type required so HC can emit the schema field.
/// </summary>
public record SubscriptionDiscount(
    long Amount,
    string Name,
    double Percent,
    DateTime? Until);

/// <summary>
/// Invoice summary — always null in HoldFast (no SaaS billing).
/// Concrete type required so HC can emit the schema field.
/// </summary>
public record Invoice(
    [property: GraphQLName("amountDue")] long? AmountDue,
    [property: GraphQLName("amountPaid")] long? AmountPaid,
    [property: GraphQLName("attemptCount")] long? AttemptCount,
    DateTime? Date,
    string? Status,
    string? Url);

/// <summary>
/// Stub subscription details — HoldFast has no SaaS billing.
/// Fields use camelCase per Go schema.
/// </summary>
public record SubscriptionDetails(
    [property: GraphQLName("baseAmount")] long BaseAmount,
    SubscriptionDiscount? Discount,
    [property: GraphQLName("lastInvoice")] Invoice? LastInvoice,
    [property: GraphQLName("billingIssue")] bool BillingIssue,
    [property: GraphQLName("billingIngestBlocked")] bool BillingIngestBlocked);

/// <summary>
/// Webhook destination — url and optional authorization header.
/// Used by legacy alert types (ErrorAlert, SessionAlert, LogAlert).
/// </summary>
public record WebhookDestinationGql(string Url, string? Authorization);

/// <summary>
/// Saved segment with parsed params for GraphQL schema compatibility.
/// Go schema exposes params as an object with a query field, not a raw string.
/// </summary>
public record SavedSegmentGql(
    int Id,
    string Name,
    [property: GraphQLName("params")] SavedSegmentParams? Params);

/// <summary>
/// Parsed params for a saved segment.
/// </summary>
public record SavedSegmentParams(string? Query);

/// <summary>
/// Stub billing details — returns zero meters and unlimited plan for self-hosted deployments.
/// All billing was removed from HoldFast; this stub satisfies the frontend contract.
/// Daily average fields always return 0 (no metering).
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
    [property: GraphQLName("metricsBillingLimit")] long MetricsBillingLimit,
    [property: GraphQLName("sessionsDailyAverage")] double SessionsDailyAverage = 0,
    [property: GraphQLName("errorsDailyAverage")] double ErrorsDailyAverage = 0,
    [property: GraphQLName("logsDailyAverage")] double LogsDailyAverage = 0,
    [property: GraphQLName("tracesDailyAverage")] double TracesDailyAverage = 0,
    [property: GraphQLName("metricsDailyAverage")] double MetricsDailyAverage = 0);

/// <summary>
/// Sampling rates and limits for a project. Matches Go schema Sampling type.
/// </summary>
public record SamplingResult(
    [property: GraphQLName("session_sampling_rate")] double SessionSamplingRate,
    [property: GraphQLName("error_sampling_rate")] double ErrorSamplingRate,
    [property: GraphQLName("log_sampling_rate")] double LogSamplingRate,
    [property: GraphQLName("trace_sampling_rate")] double TraceSamplingRate,
    [property: GraphQLName("metric_sampling_rate")] double MetricSamplingRate,
    [property: GraphQLName("session_minute_rate_limit")] long? SessionMinuteRateLimit,
    [property: GraphQLName("error_minute_rate_limit")] long? ErrorMinuteRateLimit,
    [property: GraphQLName("log_minute_rate_limit")] long? LogMinuteRateLimit,
    [property: GraphQLName("trace_minute_rate_limit")] long? TraceMinuteRateLimit,
    [property: GraphQLName("metric_minute_rate_limit")] long? MetricMinuteRateLimit,
    [property: GraphQLName("session_exclusion_query")] string? SessionExclusionQuery,
    [property: GraphQLName("error_exclusion_query")] string? ErrorExclusionQuery,
    [property: GraphQLName("log_exclusion_query")] string? LogExclusionQuery,
    [property: GraphQLName("trace_exclusion_query")] string? TraceExclusionQuery,
    [property: GraphQLName("metric_exclusion_query")] string? MetricExclusionQuery);

/// <summary>
/// Combined project settings response — matches Go schema AllProjectSettings type.
/// Merges Project entity fields with ProjectFilterSettings sampling/filter config.
/// </summary>
[GraphQLName("AllProjectSettings")]
public record AllProjectSettings(
    int Id,
    string Name,
    [property: GraphQLName("verbose_id")] string VerboseId,
    [property: GraphQLName("billing_email")] string? BillingEmail,
    [property: GraphQLName("workspace_id")] string WorkspaceId,
    [property: GraphQLName("excluded_users")] List<string> ExcludedUsers,
    [property: GraphQLName("error_filters")] List<string> ErrorFilters,
    [property: GraphQLName("error_json_paths")] List<string> ErrorJsonPaths,
    [property: GraphQLName("rage_click_window_seconds")] int RageClickWindowSeconds,
    [property: GraphQLName("rage_click_radius_pixels")] int RageClickRadiusPixels,
    [property: GraphQLName("rage_click_count")] int RageClickCount,
    [property: GraphQLName("filter_chrome_extension")] bool? FilterChromeExtension,
    [property: GraphQLName("filterSessionsWithoutError")] bool FilterSessionsWithoutError,
    [property: GraphQLName("autoResolveStaleErrorsDayInterval")] int AutoResolveStaleErrorsDayInterval,
    SamplingResult Sampling);

/// <summary>
/// Sort direction for paginated queries. Matches Go schema SortDirection enum.
/// HC GetEnumValueName returns value.ToString(), so ASC/DESC stay uppercase to match the frontend.
/// </summary>
public enum SortDirection { ASC, DESC }

/// <summary>
/// A single log-level count within a histogram time bucket.
/// </summary>
public record LogsBucketCount(long Count, string Level);

/// <summary>
/// A time bucket in the logs histogram, identified by index and containing per-level counts.
/// </summary>
public record LogsBucketGroup(
    [property: GraphQLName("bucketId")] long BucketId,
    List<LogsBucketCount> Counts);

/// <summary>
/// Logs histogram result — matches Go schema LogsHistogram type.
/// </summary>
public record LogsHistogramResult(
    [property: GraphQLName("totalCount")] long TotalCount,
    List<LogsBucketGroup> Buckets,
    [property: GraphQLName("objectCount")] long ObjectCount,
    [property: GraphQLName("sampleFactor")] double SampleFactor);

// ── Metrics response types ────────────────────────────────────────────

/// <summary>
/// Input for individual metric expressions (aggregator + column).
/// Matches Go schema MetricExpressionInput.
/// </summary>
public class MetricExpressionInput
{
    public MetricAggregator Aggregator { get; set; }
    public string Column { get; set; } = string.Empty;
}

/// <summary>
/// Prediction / anomaly-detection settings for metrics queries.
/// Matches Go schema PredictionSettings.
/// HC snake_case: changepointPriorScale → changepointpriorscale — override with [GraphQLName].
/// </summary>
public class PredictionSettings
{
    [GraphQLName("changepointPriorScale")]
    public double ChangepointPriorScale { get; set; }
    [GraphQLName("intervalSeconds")]
    public int IntervalSeconds { get; set; }
    [GraphQLName("intervalWidth")]
    public double IntervalWidth { get; set; }
    [GraphQLName("thresholdCondition")]
    public string ThresholdCondition { get; set; } = string.Empty;
}

/// <summary>
/// Single time-bucket in a metrics result.
/// Matches Go schema MetricBucket type (fields are camelCase-ish in Go, override HC snake_case).
/// </summary>
[GraphQLName("MetricBucket")]
public class MetricBucketResult
{
    [GraphQLName("bucket_id")]
    public long BucketId { get; init; }
    [GraphQLName("bucket_min")]
    public double? BucketMin { get; init; }
    [GraphQLName("bucket_max")]
    public double? BucketMax { get; init; }
    [GraphQLName("bucket_value")]
    public double? BucketValue { get; init; }
    public string Column { get; init; } = string.Empty;
    public List<string> Group { get; init; } = [];
    [GraphQLName("metric_type")]
    public MetricAggregator MetricType { get; init; }
    [GraphQLName("metric_value")]
    public double? MetricValue { get; init; }
    [GraphQLName("yhat_lower")]
    public double? YhatLower { get; init; }
    [GraphQLName("yhat_upper")]
    public double? YhatUpper { get; init; }
}

/// <summary>
/// Metrics query result — list of time buckets plus totals.
/// Matches Go schema MetricsBuckets type.
/// </summary>
[GraphQLName("MetricsBuckets")]
public class MetricsBucketsResult
{
    [GraphQLName("bucket_count")]
    public long BucketCount { get; init; }
    [GraphQLName("sample_factor")]
    public double SampleFactor { get; init; } = 1.0;
    public List<MetricBucketResult> Buckets { get; init; } = [];

    /// <summary>Build from the ClickHouse internal model.</summary>
    public static MetricsBucketsResult FromClickHouse(MetricsBuckets src) =>
        new()
        {
            BucketCount = src.TotalCount,
            SampleFactor = src.SampleFactor ?? 1.0,
            Buckets = src.Buckets.Select((b, i) => new MetricBucketResult
            {
                BucketId = (long)i,
                BucketValue = b.Value,
                Column = "value",
                Group = b.Group != null ? [b.Group] : [],
                MetricType = MetricAggregator.Count,
                MetricValue = b.MetricValue,
            }).ToList(),
        };
}

/// <summary>
/// Errors histogram result. Matches Go schema ErrorsHistogram type.
/// </summary>
public class ErrorsHistogram
{
    [GraphQLName("bucket_times")]
    public List<DateTime> BucketTimes { get; init; } = [];
    [GraphQLName("error_objects")]
    public List<long> ErrorObjects { get; init; } = [];
}

/// <summary>
/// A tag/op/value filter applied to metric monitor queries.
/// </summary>
public record MetricMonitorFilter(string Tag, string Op, string Value);

/// <summary>
/// A session track property used to filter session alerts.
/// </summary>
public record TrackProperty(int Id, string Name, string Value);

/// <summary>
/// A user property used to filter session alerts.
/// </summary>
public record UserProperty(int Id, string Name, string Value);

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
