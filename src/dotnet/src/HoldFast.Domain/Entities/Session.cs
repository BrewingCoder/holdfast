namespace HoldFast.Domain.Entities;

public class Session : BaseEntity
{
    public string SecureId { get; set; } = string.Empty;
    public string? Fingerprint { get; set; }
    public string? OSName { get; set; }
    public string? OSVersion { get; set; }
    public string? BrowserName { get; set; }
    public string? BrowserVersion { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? Postal { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Identifier { get; set; }
    public string? Language { get; set; }
    public string? IP { get; set; }
    public int ProjectId { get; set; }
    public int? ViewedByAdmins { get; set; }
    public bool? Starred { get; set; }
    public bool? Processed { get; set; }
    public bool? Excluded { get; set; }
    public bool? HasErrors { get; set; }
    public bool? HasRageClicks { get; set; }
    public int? FieldGroup { get; set; }
    public int? FirstTime { get; set; }
    public int? PagesVisited { get; set; }
    public int? ActiveLength { get; set; }
    public int? Length { get; set; }
    public bool? WithinBillingQuota { get; set; }
    public string? ClientID { get; set; }
    public string? Environment { get; set; }
    public string? AppVersion { get; set; }
    public string? ServiceName { get; set; }
    public string? ClientVersion { get; set; }
    public string? FirstloadVersion { get; set; }
    public bool? EnableStrictPrivacy { get; set; }
    public bool? EnableRecordingNetworkContents { get; set; }
    public string? PrivacySetting { get; set; }
    public string? ClientConfig { get; set; }
    public bool? ObjectStorageEnabled { get; set; }
    public bool? DirectDownloadEnabled { get; set; }
    public bool? PayloadUpdated { get; set; }
    public int? PayloadSize { get; set; }
    public string? LastUserInteractionTime { get; set; }
    public double? Normalness { get; set; }
    public bool? Chunked { get; set; }
    public string? Lock { get; set; }
    public int? RetryCount { get; set; }

    // Navigation
    public Project Project { get; set; } = null!;
}

public class SessionInterval : BaseEntity
{
    public int SessionId { get; set; }
    public int StartTime { get; set; }
    public int EndTime { get; set; }
    public int Duration { get; set; }
    public bool Active { get; set; }

    public Session Session { get; set; } = null!;
}

public class SessionExport : BaseEntity
{
    public int SessionId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string? Error { get; set; }

    public Session Session { get; set; } = null!;
}

public class SessionInsight : BaseEntity
{
    public int SessionId { get; set; }
    public string? Insight { get; set; }

    public Session Session { get; set; } = null!;
}

public class SessionAdminsView : BaseEntity
{
    public int SessionId { get; set; }
    public int AdminId { get; set; }

    public Session Session { get; set; } = null!;
    public Admin Admin { get; set; } = null!;
}

public class EventChunk : BaseEntity
{
    public int SessionId { get; set; }
    public int ChunkIndex { get; set; }
    public long Timestamp { get; set; }

    public Session Session { get; set; } = null!;
}

public class RageClickEvent : BaseEntity
{
    public int ProjectId { get; set; }
    public int SessionId { get; set; }
    public int TotalClicks { get; set; }
    public string? Selector { get; set; }
    public long StartTimestamp { get; set; }
    public long EndTimestamp { get; set; }

    public Session Session { get; set; } = null!;
}
