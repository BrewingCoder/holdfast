namespace HoldFast.Domain.Entities;

public class SessionComment : BaseEntity
{
    public int ProjectId { get; set; }
    public int SessionId { get; set; }
    public int AdminId { get; set; }
    public int SessionSecureId { get; set; }
    public int Timestamp { get; set; }
    public string? Text { get; set; }
    public double XCoordinate { get; set; }
    public double YCoordinate { get; set; }
    public string? Type { get; set; }
    public string? Metadata { get; set; }

    // Navigation
    public Session Session { get; set; } = null!;
    public Admin Admin { get; set; } = null!;
    public ICollection<SessionCommentTag> Tags { get; set; } = [];
    public ICollection<CommentReply> Replies { get; set; } = [];
    public ICollection<CommentFollower> Followers { get; set; } = [];
}

public class SessionCommentTag : BaseEntity
{
    public int SessionCommentId { get; set; }
    public string? Name { get; set; }

    public SessionComment SessionComment { get; set; } = null!;
}

public class CommentReply : BaseEntity
{
    public int SessionCommentId { get; set; }
    public int? ErrorCommentId { get; set; }
    public int AdminId { get; set; }
    public string? Text { get; set; }

    public Admin Admin { get; set; } = null!;
}

public class CommentFollower : BaseEntity
{
    public int SessionCommentId { get; set; }
    public int? ErrorCommentId { get; set; }
    public int AdminId { get; set; }
    public bool HasMuted { get; set; }

    public Admin Admin { get; set; } = null!;
}

public class CommentSlackThread : BaseEntity
{
    public int SessionCommentId { get; set; }
    public int? ErrorCommentId { get; set; }
    public string? SlackChannelId { get; set; }
    public string? ThreadTs { get; set; }
}
