namespace HoldFast.Domain.Entities;

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

public class Field : BaseEntity
{
    public int ProjectId { get; set; }
    public string? Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class EnhancedUserDetails : BaseEntity
{
    public string? Email { get; set; }
    public string? PersonJson { get; set; }
    public string? CompanyJson { get; set; }
}

public class RegistrationData : BaseEntity
{
    public int WorkspaceId { get; set; }
    public string? TeamSize { get; set; }
    public string? Role { get; set; }
    public string? UseCase { get; set; }
    public string? HeardAbout { get; set; }
    public string? Pun { get; set; }
}

public class SavedSegment : BaseEntity
{
    public int ProjectId { get; set; }
    public string? Name { get; set; }
    public string? Params { get; set; }
    public string? EntityType { get; set; }
}

public class SavedAsset : BaseEntity
{
    public int ProjectId { get; set; }
    public string? OriginalUrl { get; set; }
    public string? SavedUrl { get; set; }
    public string? HashVal { get; set; }
}

public class ProjectAssetTransform : BaseEntity
{
    public int ProjectId { get; set; }
    public string? Name { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
}

public class DailySessionCount : BaseEntity
{
    public int ProjectId { get; set; }
    public DateTime Date { get; set; }
    public int Count { get; set; }
}

public class DailyErrorCount : BaseEntity
{
    public int ProjectId { get; set; }
    public DateTime Date { get; set; }
    public int Count { get; set; }
}

public class EmailSignup : BaseEntity
{
    public string Email { get; set; } = string.Empty;
    public string? AdsenseAction { get; set; }
}

public class EmailOptOut : BaseEntity
{
    public int AdminId { get; set; }
    public string Category { get; set; } = string.Empty;

    public Admin Admin { get; set; } = null!;
}

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

public class DeleteSessionsTask : BaseEntity
{
    public int ProjectId { get; set; }
    public int SessionCount { get; set; }
    public DateTime? TaskDate { get; set; }
}

public class UserJourneyStep : BaseEntity
{
    public int ProjectId { get; set; }
    public int SessionId { get; set; }
    public int StepIndex { get; set; }
    public string? Url { get; set; }
}

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
}

public class LogAdminsView : BaseEntity
{
    public int LogId { get; set; }
    public int AdminId { get; set; }
}
