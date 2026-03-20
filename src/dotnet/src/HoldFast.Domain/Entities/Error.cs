using HoldFast.Domain.Enums;

namespace HoldFast.Domain.Entities;

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
}

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

public class ErrorFingerprint : BaseEntity
{
    public int ErrorGroupId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public int? Index { get; set; }

    public ErrorGroup ErrorGroup { get; set; } = null!;
}

public class ErrorGroupEmbeddings : BaseEntity
{
    public int ErrorGroupId { get; set; }
    public int ProjectId { get; set; }
    public string? GTELargeEmbedding { get; set; } // pgvector

    public ErrorGroup ErrorGroup { get; set; } = null!;
}

public class ErrorTag : BaseEntity
{
    public int ErrorGroupId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    public ErrorGroup ErrorGroup { get; set; } = null!;
}

public class ErrorComment : BaseEntity
{
    public int ErrorGroupId { get; set; }
    public int AdminId { get; set; }
    public string? Text { get; set; }

    public ErrorGroup ErrorGroup { get; set; } = null!;
    public Admin Admin { get; set; } = null!;
}

public class ErrorGroupActivityLog : BaseEntity
{
    public int ErrorGroupId { get; set; }
    public int? AdminId { get; set; }
    public string Action { get; set; } = string.Empty;

    public ErrorGroup ErrorGroup { get; set; } = null!;
}

public class ErrorGroupAdminsView : BaseEntity
{
    public int ErrorGroupId { get; set; }
    public int AdminId { get; set; }

    public ErrorGroup ErrorGroup { get; set; } = null!;
    public Admin Admin { get; set; } = null!;
}

public class ExternalAttachment : BaseEntity
{
    public int? ErrorGroupId { get; set; }
    public int? SessionCommentId { get; set; }
    public string IntegrationType { get; set; } = string.Empty;
    public string? ExternalId { get; set; }
    public string? Title { get; set; }

    public ErrorGroup? ErrorGroup { get; set; }
}
