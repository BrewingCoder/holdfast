using System.ComponentModel.DataAnnotations.Schema;
using HoldFast.Domain.Enums;

namespace HoldFast.Domain.Entities;

/// <summary>
/// Groups related error instances by fingerprint. Represents one distinct error
/// (e.g., "TypeError: Cannot read property 'x' of undefined"). State tracks the
/// lifecycle: Open → Resolved/Ignored.
/// </summary>
public class ErrorGroup : BaseEntity
{
    public int ProjectId { get; set; }
    public string? Event { get; set; }
    public string? Type { get; set; }
    public ErrorGroupState State { get; set; } = ErrorGroupState.Open;
    public string? MappedStackTrace { get; set; }
    public string? StackTrace { get; set; }
    public string? Fields { get; set; }
    public string? Environments { get; set; }
    public string SecureId { get; set; } = string.Empty;
    public bool? IsPublic { get; set; }
    public string? ServiceName { get; set; }
    public int? SnoozedUntil { get; set; }

    // Navigation
    public Project Project { get; set; } = null!;
    public ICollection<ErrorObject> ErrorObjects { get; set; } = [];
    public ICollection<ErrorFingerprint> Fingerprints { get; set; } = [];

    // ── Computed / stub fields for GraphQL schema compatibility ─────────────
    // These are not stored in PostgreSQL; they are populated by resolvers or
    // left as null/empty stubs so the HC schema matches the Go/gqlgen contract.
    [NotMapped] public DateTime? FirstOccurrence { get; set; }
    [NotMapped] public DateTime? LastOccurrence { get; set; }
    [NotMapped] public List<long> ErrorFrequency { get; set; } = [];
    [NotMapped] public bool? Viewed { get; set; }
}

/// <summary>
/// A single error occurrence with full stack trace and metadata. Many ErrorObjects
/// belong to one ErrorGroup. Contains source mapping info for deobfuscated stack traces.
/// </summary>
public class ErrorObject : BaseEntity
{
    public int ProjectId { get; set; }
    public int? SessionId { get; set; }
    public int? TraceId { get; set; }
    public int ErrorGroupId { get; set; }
    public string? Event { get; set; }
    public string? Type { get; set; }
    public string? Url { get; set; }
    public string? Source { get; set; }
    public string? LineColumnNumber { get; set; }
    public string? StackTrace { get; set; }
    public string? MappedStackTrace { get; set; }
    public DateTime Timestamp { get; set; }
    public string? Payload { get; set; }
    public string? Environment { get; set; }
    public string? OS { get; set; }
    public string? Browser { get; set; }
    public string? RequestId { get; set; }
    public bool? IsBeacon { get; set; }
    public string? ServiceName { get; set; }
    public string? ServiceVersion { get; set; }
    public string? SpanId { get; set; }
    public string? TraceExternalId { get; set; }

    // Navigation
    public ErrorGroup ErrorGroup { get; set; } = null!;
    public Session? Session { get; set; }
}

/// <summary>
/// Fingerprint used to group error instances. Type indicates the fingerprinting
/// strategy (e.g., "json_result", "stack_frame"). Index orders multiple fingerprints.
/// </summary>
public class ErrorFingerprint : BaseEntity
{
    public int ProjectId { get; set; }
    public int ErrorGroupId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public int? Index { get; set; }

    public ErrorGroup ErrorGroup { get; set; } = null!;
}

/// <summary>
/// Vector embedding of an error group for similarity-based grouping (pgvector).
/// Future feature: embedding-based error deduplication.
/// </summary>
public class ErrorGroupEmbeddings : BaseEntity
{
    public int ErrorGroupId { get; set; }
    public int ProjectId { get; set; }
    public string? GTELargeEmbedding { get; set; } // pgvector

    public ErrorGroup ErrorGroup { get; set; } = null!;
}

/// <summary>
/// User-assigned tag on an error group for categorization and filtering.
/// </summary>
public class ErrorTag : BaseEntity
{
    public int ErrorGroupId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    public ErrorGroup ErrorGroup { get; set; } = null!;
}

/// <summary>
/// Comment on an error group thread. Supports discussion between admins about an error.
/// </summary>
public class ErrorComment : BaseEntity
{
    public int ErrorGroupId { get; set; }
    public int AdminId { get; set; }
    public string? Text { get; set; }

    public ErrorGroup ErrorGroup { get; set; } = null!;
    public Admin Admin { get; set; } = null!;
}

/// <summary>
/// Audit log entry for error group state changes (e.g., "Resolved" by admin).
/// </summary>
public class ErrorGroupActivityLog : BaseEntity
{
    public int ErrorGroupId { get; set; }
    public int? AdminId { get; set; }
    public string Action { get; set; } = string.Empty;

    public ErrorGroup ErrorGroup { get; set; } = null!;
}

/// <summary>
/// Tracks which admins have viewed an error group (for "new" badges in the UI).
/// </summary>
public class ErrorGroupAdminsView : BaseEntity
{
    public int ErrorGroupId { get; set; }
    public int AdminId { get; set; }

    public ErrorGroup ErrorGroup { get; set; } = null!;
    public Admin Admin { get; set; } = null!;
}

/// <summary>
/// Links an error group or session comment to an external issue tracker (e.g., Linear, Jira).
/// </summary>
public class ExternalAttachment : BaseEntity
{
    public int? ErrorGroupId { get; set; }
    public int? SessionCommentId { get; set; }
    public string IntegrationType { get; set; } = string.Empty;
    public string? ExternalId { get; set; }
    public string? Title { get; set; }

    public ErrorGroup? ErrorGroup { get; set; }
}
