namespace HoldFast.Domain.Entities;

/// <summary>
/// An authenticated user account. Maps to the "admins" table (legacy name from Highlight.io).
/// Admins belong to workspaces and have roles within them.
/// </summary>
public class Admin : BaseEntity
{
    public string? Uid { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? PhotoUrl { get; set; }
    public string? AboutYouDetailsFilled { get; set; }
    public string? UserDefinedRole { get; set; }
    public string? UserDefinedPersona { get; set; }
    public string? Referral { get; set; }
    public bool EmailVerified { get; set; }
    public bool SlackIMChannelID { get; set; }

    // Navigation
    public ICollection<Organization> Organizations { get; set; } = [];
    public ICollection<Workspace> Workspaces { get; set; } = [];
}

/// <summary>
/// Join table linking admins to workspaces with a role (ADMIN or MEMBER).
/// Uses composite key (AdminId, WorkspaceId). ProjectIds restricts access to specific projects.
/// </summary>
public class WorkspaceAdmin
{
    public int AdminId { get; set; }
    public int WorkspaceId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? Role { get; set; } = WorkspaceRoles.Admin;
    public List<int>? ProjectIds { get; set; }

    // Navigation
    public Admin Admin { get; set; } = null!;
    public Workspace Workspace { get; set; } = null!;
}

/// <summary>
/// Pending invitation for a user to join a workspace. Contains a unique secret
/// used as the invite URL token. Expires after ExpirationDate (default 7 days).
/// </summary>
public class WorkspaceInviteLink : BaseEntity
{
    public int? WorkspaceId { get; set; }
    public string? InviteeEmail { get; set; }
    public string? InviteeRole { get; set; }
    public DateTime? ExpirationDate { get; set; }
    public string? Secret { get; set; }
    public List<int>? ProjectIds { get; set; }

    // Navigation
    public Workspace? Workspace { get; set; }
}

/// <summary>
/// Tracks a request from an admin to join a workspace (for auto-join flows).
/// </summary>
public class WorkspaceAccessRequest : BaseEntity
{
    public int AdminId { get; set; }
    public int LastRequestedWorkspace { get; set; }
}
