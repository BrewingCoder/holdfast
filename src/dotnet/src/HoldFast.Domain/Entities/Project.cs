namespace HoldFast.Domain.Entities;

public class Project : BaseEntity
{
    public string? Name { get; set; }
    public string? ZapierAccessToken { get; set; }
    public string? BillingEmail { get; set; }
    public string? Secret { get; set; }
    public DateTime? TrialEndDate { get; set; }
    public int WorkspaceId { get; set; }
    public bool FreeTier { get; set; }
    public List<string> ExcludedUsers { get; set; } = [];
    public List<string> ErrorFilters { get; set; } = [];
    public List<string> ErrorJsonPaths { get; set; } = [];
    public List<string> Platforms { get; set; } = [];
    public bool? BackendSetup { get; set; }
    public int RageClickWindowSeconds { get; set; } = 5;
    public int RageClickRadiusPixels { get; set; } = 8;
    public int RageClickCount { get; set; } = 5;
    public bool? FilterChromeExtension { get; set; } = false;

    // Navigation
    public Workspace Workspace { get; set; } = null!;
    public ICollection<SetupEvent> SetupEvents { get; set; } = [];
}

public class SetupEvent
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public int ProjectId { get; set; }
    public string Type { get; set; } = string.Empty;

    public Project Project { get; set; } = null!;
}

public class ProjectFilterSettings : BaseEntity
{
    public int ProjectId { get; set; }
    public bool FilterSessionsWithoutError { get; set; }
    public int AutoResolveStaleErrorsDayInterval { get; set; }
    public double SessionSamplingRate { get; set; } = 1.0;
    public double ErrorSamplingRate { get; set; } = 1.0;
    public double LogSamplingRate { get; set; } = 1.0;
    public double TraceSamplingRate { get; set; } = 1.0;
    public double MetricSamplingRate { get; set; } = 1.0;
    public long? SessionMinuteRateLimit { get; set; }
    public long? ErrorMinuteRateLimit { get; set; }
    public long? LogMinuteRateLimit { get; set; }
    public long? TraceMinuteRateLimit { get; set; }
    public long? MetricMinuteRateLimit { get; set; }
    public string? SessionExclusionQuery { get; set; }
    public string? ErrorExclusionQuery { get; set; }
    public string? LogExclusionQuery { get; set; }
    public string? TraceExclusionQuery { get; set; }
    public string? MetricExclusionQuery { get; set; }

    public Project Project { get; set; } = null!;
}

public class ProjectClientSamplingSettings : BaseEntity
{
    public int ProjectId { get; set; }
    public string? SpanSamplingConfigs { get; set; }
    public string? LogSamplingConfigs { get; set; }

    public Project Project { get; set; } = null!;
}
