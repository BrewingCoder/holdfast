namespace HoldFast.Domain.Enums;

/// <summary>
/// Plan tiers from the upstream Highlight.io billing model. In HoldFast all workspaces
/// are Enterprise tier. Kept for database/migration compatibility only.
/// </summary>
public enum PlanType
{
    Free,
    Lite,
    Basic,
    Startup,
    Enterprise,
    UsageBased,
    Graduated
}
