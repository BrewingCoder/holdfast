using System.ComponentModel.DataAnnotations.Schema;

namespace HoldFast.Domain.Entities;

/// <summary>
/// A user session recording. Core entity for session replay — stores device info,
/// geo-location, processing state, and replay metadata. SecureId is the public-facing ID
/// used by SDKs; the integer Id is internal only.
/// </summary>
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

    // Computed / stub fields for HC schema compatibility
    [NotMapped] public bool? Identified => !string.IsNullOrEmpty(Identifier);
    // Populated by the resolver from SessionAdminsView; null until hydrated.
    [NotMapped] public bool? Viewed { get; set; }
    // Legacy fields from Go schema — always null/false stubs for self-hosted.
    [NotMapped] public string? UserProperties { get; set; }
    [NotMapped] public string? EventCounts { get; set; }
    [NotMapped] public bool IsPublic => false;
    [NotMapped] public string? Email { get; set; }
}

/// <summary>
/// A time interval within a session, used for timeline visualization.
/// StartTime/EndTime/Duration are in milliseconds (epoch-relative).
/// </summary>
public class SessionInterval : BaseEntity
{
    public int SessionId { get; set; }
    public int StartTime { get; set; }
    public int EndTime { get; set; }
    public int Duration { get; set; }
    public bool Active { get; set; }

    public Session Session { get; set; } = null!;
}

/// <summary>
/// A session export request (e.g., mp4 video). Created by the user, processed by a background worker.
/// </summary>
public class SessionExport : BaseEntity
{
    public int SessionId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string? Error { get; set; }

    public Session Session { get; set; } = null!;
}

/// <summary>
/// AI-generated insight for a session (e.g., "user encountered errors on checkout page").
/// </summary>
public class SessionInsight : BaseEntity
{
    public int SessionId { get; set; }
    public string? Insight { get; set; }

    public Session Session { get; set; } = null!;
}

/// <summary>
/// Tracks which admins have viewed a session (for "viewed" badges in the UI).
/// </summary>
public class SessionAdminsView : BaseEntity
{
    public int SessionId { get; set; }
    public int AdminId { get; set; }

    public Session Session { get; set; } = null!;
    public Admin Admin { get; set; } = null!;
}

/// <summary>
/// A chunk of session replay events stored in object storage. Sessions are split into
/// chunks for streaming playback. ChunkIndex determines ordering; Timestamp is the
/// epoch time of the chunk's first event.
/// </summary>
public class EventChunk : BaseEntity
{
    public int SessionId { get; set; }
    public int ChunkIndex { get; set; }
    public long Timestamp { get; set; }

    public Session Session { get; set; } = null!;
}

/// <summary>
/// Detected rage click event — multiple rapid clicks on the same element.
/// Used for UX frustration analysis.
/// </summary>
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
