namespace HoldFast.Domain.Entities;

public class IntegrationProjectMapping : BaseEntity
{
    public string IntegrationType { get; set; } = string.Empty;
    public int ProjectId { get; set; }
    public string? ExternalId { get; set; }

    public Project Project { get; set; } = null!;
}

public class IntegrationWorkspaceMapping : BaseEntity
{
    public string IntegrationType { get; set; } = string.Empty;
    public int WorkspaceId { get; set; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? Expiry { get; set; }

    public Workspace Workspace { get; set; } = null!;
}

public class VercelIntegrationConfig : BaseEntity
{
    public int ProjectId { get; set; }
    public int WorkspaceId { get; set; }
    public string? VercelProjectId { get; set; }

    public Project Project { get; set; } = null!;
}

public class ResthookSubscription : BaseEntity
{
    public int ProjectId { get; set; }
    public string? Event { get; set; }
    public string? TargetUrl { get; set; }
}

public class OAuthClientStore : BaseEntity
{
    public string ClientId { get; set; } = string.Empty;
    public string? Secret { get; set; }
    public string? Domains { get; set; }
    public string? AppName { get; set; }
    public int? AdminId { get; set; }
    public int? WorkspaceId { get; set; }
    public int? CreatorAdminId { get; set; }

    public Admin? Admin { get; set; }
}

public class OAuthOperation : BaseEntity
{
    public int ClientId { get; set; }
    public string? AuthorizedGraphQLOperation { get; set; }
    public int? MinuteRateLimit { get; set; }
}

public class SSOClient : BaseEntity
{
    public int WorkspaceId { get; set; }
    public string? Domain { get; set; }
    public string? ProviderUrl { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    public Workspace Workspace { get; set; } = null!;
}
