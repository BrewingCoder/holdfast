using HashidsNet;

namespace HoldFast.Domain.Entities;

public class Project : BaseEntity
{
    /// <summary>
    /// Shared Hashids instance matching Go's configuration:
    /// no salt, MinLength=8, alphabet=abcdefghijklmnopqrstuvwxyz1234567890
    /// </summary>
    private static readonly Hashids HashIdEncoder = new(
        salt: "",
        minHashLength: 8,
        alphabet: "abcdefghijklmnopqrstuvwxyz1234567890");

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

    /// <summary>
    /// Computed verbose ID matching Go's HashID encoding.
    /// Used by SDKs as the project identifier (organization_verbose_id).
    /// </summary>
    public string VerboseId => HashIdEncoder.Encode(Id);

    /// <summary>
    /// Parse a verbose ID back to a numeric project ID.
    /// Falls back to plain integer parsing for legacy/out-of-date clients.
    /// </summary>
    public static int FromVerboseId(string verboseId)
    {
        // Legacy clients may send plain integer IDs
        if (int.TryParse(verboseId, out var plainId))
            return plainId;

        var decoded = HashIdEncoder.Decode(verboseId);
        if (decoded.Length != 1)
            throw new ArgumentException($"Invalid verbose ID: {verboseId}");
        return decoded[0];
    }

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
