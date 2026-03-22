namespace HoldFast.Domain.Entities;

/// <summary>
/// Feature flags and AI settings for a workspace. All flags default to true for self-hosted
/// deployments (no billing tiers). One-to-one with Workspace via unique index on WorkspaceId.
/// </summary>
public class AllWorkspaceSettings : BaseEntity
{
    public int WorkspaceId { get; set; }

    // AI features
    public bool AIApplication { get; set; } = true;
    public bool AIInsights { get; set; } = true;
    public bool AIQueryBuilder { get; set; } = true;

    // Error embeddings
    public bool ErrorEmbeddingsGroup { get; set; } = true;
    public bool ErrorEmbeddingsTagGroup { get; set; } = true;
    public double ErrorEmbeddingsThreshold { get; set; } = 0.2;

    // Feature flags (all enabled for self-hosted)
    public bool ReplaceAssets { get; set; } = true;
    public bool StoreIP { get; set; } = true;
    public bool EnableUnlimitedDashboards { get; set; } = true;
    public bool EnableUnlimitedProjects { get; set; } = true;
    public bool EnableUnlimitedRetention { get; set; } = true;
    public bool EnableUnlimitedSeats { get; set; } = true;
    public bool EnableBillingLimits { get; set; } = true;
    public bool EnableGrafanaDashboard { get; set; } = true;
    public bool EnableIngestSampling { get; set; } = true;
    public bool EnableProjectLevelAccess { get; set; } = true;
    public bool EnableSessionExport { get; set; } = true;
    public bool EnableSSO { get; set; } = true;
    public bool EnableDataDeletion { get; set; } = true;
    public bool EnableNetworkTraces { get; set; } = true;
    public bool EnableUnlistedSharing { get; set; } = true;
    public bool EnableJiraIntegration { get; set; } = true;
    public bool EnableTeamsIntegration { get; set; } = true;
    public bool EnableLogTraceIngestion { get; set; } = true;

    public Workspace Workspace { get; set; } = null!;
}

/// <summary>
/// A key-value metadata field attached to sessions within a project (e.g., user email,
/// plan tier). Used for search filtering and saved segments.
/// </summary>
public class Field : BaseEntity
{
    public int ProjectId { get; set; }
    public string? Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// Enriched user/company data looked up by email. PersonJson and CompanyJson store
/// the full enrichment payloads. Unique index on Email.
/// </summary>
public class EnhancedUserDetails : BaseEntity
{
    public string? Email { get; set; }
    public string? PersonJson { get; set; }
    public string? CompanyJson { get; set; }
}

/// <summary>
/// Onboarding survey data collected when a workspace is created (team size, role, use case).
/// </summary>
public class RegistrationData : BaseEntity
{
    public int WorkspaceId { get; set; }
    public string? TeamSize { get; set; }
    public string? Role { get; set; }
    public string? UseCase { get; set; }
    public string? HeardAbout { get; set; }
    public string? Pun { get; set; }
}

/// <summary>
/// A named, saved search filter (segment) for sessions or errors. Params stores the
/// serialized filter configuration. EntityType distinguishes session vs error segments.
/// </summary>
public class SavedSegment : BaseEntity
{
    public int ProjectId { get; set; }
    public string? Name { get; set; }
    public string? Params { get; set; }
    public string? EntityType { get; set; }
}

/// <summary>
/// A cached copy of an external asset (CSS, image) used during session replay. OriginalUrl
/// is the source; SavedUrl points to the local copy. HashVal deduplicates downloads.
/// </summary>
public class SavedAsset : BaseEntity
{
    public int ProjectId { get; set; }
    public string? OriginalUrl { get; set; }
    public string? SavedUrl { get; set; }
    public string? HashVal { get; set; }
}

/// <summary>
/// URL rewrite rule for session replay assets. Transforms asset URLs from From pattern
/// to To pattern (e.g., replacing CDN domains with local proxies).
/// </summary>
public class ProjectAssetTransform : BaseEntity
{
    public int ProjectId { get; set; }
    public string? Name { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
}

/// <summary>
/// Pre-aggregated daily session count per project, used for dashboard sparklines.
/// </summary>
public class DailySessionCount : BaseEntity
{
    public int ProjectId { get; set; }
    public DateTime Date { get; set; }
    public int Count { get; set; }
}

/// <summary>
/// Pre-aggregated daily error count per project, used for dashboard sparklines.
/// </summary>
public class DailyErrorCount : BaseEntity
{
    public int ProjectId { get; set; }
    public DateTime Date { get; set; }
    public int Count { get; set; }
}

/// <summary>
/// An email collected from the marketing signup form. AdsenseAction tracks the referral source.
/// </summary>
public class EmailSignup : BaseEntity
{
    public string Email { get; set; } = string.Empty;
    public string? AdsenseAction { get; set; }
}

/// <summary>
/// Records that an admin has opted out of a specific email notification category
/// (e.g., "Digests", "Billing"). Prevents sending that category to the admin.
/// </summary>
public class EmailOptOut : BaseEntity
{
    public int AdminId { get; set; }
    public string Category { get; set; } = string.Empty;

    public Admin Admin { get; set; } = null!;
}

/// <summary>
/// A backend service within a project (e.g., "api-gateway", "auth-service"). Tracks
/// deployment status, GitHub repo linkage, and error JSON path configuration.
/// </summary>
public class Service : BaseEntity
{
    public int ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string? GithubRepoPath { get; set; }
    public string? BuildPrefix { get; set; }
    public string? GithubPrefix { get; set; }
    public string? ErrorJsonPaths { get; set; }
}

/// <summary>
/// Background task record for bulk session deletion (data retention). Tracks the
/// number of sessions to delete and the scheduled date.
/// </summary>
public class DeleteSessionsTask : BaseEntity
{
    public int ProjectId { get; set; }
    public int SessionCount { get; set; }
    public DateTime? TaskDate { get; set; }
}

/// <summary>
/// One step in a user's navigation journey within a session. StepIndex orders
/// the pages visited; Url is the page URL at that step.
/// </summary>
public class UserJourneyStep : BaseEntity
{
    public int ProjectId { get; set; }
    public int SessionId { get; set; }
    public int StepIndex { get; set; }
    public string? Url { get; set; }
}

/// <summary>
/// Global system tuning parameters (worker counts, flush sizes/timeouts). Only the
/// row with Active=true is used. Allows runtime tuning without restarts.
/// </summary>
public class SystemConfiguration : BaseEntity
{
    public bool Active { get; set; }
    public int? MainWorkerCount { get; set; }
    public int? LogsWorkerCount { get; set; }
    public int? TracesWorkerCount { get; set; }
    public int? LogsFlushSize { get; set; }
    public int? TracesFlushSize { get; set; }
    public int? LogsFlushTimeout { get; set; }
    public int? TracesFlushTimeout { get; set; }
    public string? FilterSessionsWithoutError { get; set; }

    // Go schema compatibility fields — HoldFast does not implement maintenance windows;
    // these are always null indicating no scheduled maintenance.
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public DateTime? MaintenanceStart => null;

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public DateTime? MaintenanceEnd => null;
}

/// <summary>
/// Tracks which admins have viewed a specific log entry (for "new" badges in the UI).
/// </summary>
public class LogAdminsView : BaseEntity
{
    public int LogId { get; set; }
    public int AdminId { get; set; }
}
