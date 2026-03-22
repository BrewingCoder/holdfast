namespace HoldFast.Domain.Entities;

/// <summary>
/// Links a third-party integration (e.g., Slack, Linear) to a specific project.
/// ExternalId is the integration's identifier for this project (e.g., Slack channel ID).
/// </summary>
public class IntegrationProjectMapping : BaseEntity
{
    public string IntegrationType { get; set; } = string.Empty;
    public int ProjectId { get; set; }
    public string? ExternalId { get; set; }

    public Project Project { get; set; } = null!;
}

/// <summary>
/// Links a third-party integration to a workspace with OAuth credentials. AccessToken/RefreshToken
/// are used for API calls; Expiry tracks token lifetime for automatic refresh.
/// </summary>
public class IntegrationWorkspaceMapping : BaseEntity
{
    public string IntegrationType { get; set; } = string.Empty;
    public int WorkspaceId { get; set; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? Expiry { get; set; }

    public Workspace Workspace { get; set; } = null!;
}

/// <summary>
/// Maps a Vercel project to a HoldFast project for automatic source map uploads
/// and deployment tracking.
/// </summary>
public class VercelIntegrationConfig : BaseEntity
{
    public int ProjectId { get; set; }
    public int WorkspaceId { get; set; }
    public string? VercelProjectId { get; set; }

    public Project Project { get; set; } = null!;
}

/// <summary>
/// A webhook subscription (REST hook pattern). Sends POST requests to TargetUrl
/// when events of type Event occur in the project.
/// </summary>
public class ResthookSubscription : BaseEntity
{
    public int ProjectId { get; set; }
    public string? Event { get; set; }
    public string? TargetUrl { get; set; }
}

/// <summary>
/// Registered OAuth client application. ClientId/Secret are the credentials; Domains
/// restricts redirect URIs. CreatorAdminId tracks who registered the client.
/// </summary>
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

/// <summary>
/// Defines which GraphQL operations an OAuth client is authorized to call,
/// with an optional per-minute rate limit.
/// </summary>
public class OAuthOperation : BaseEntity
{
    public int ClientId { get; set; }
    public string? AuthorizedGraphQLOperation { get; set; }
    public int? MinuteRateLimit { get; set; }
}

/// <summary>
/// SAML/OIDC SSO configuration for a workspace. Domain determines which email addresses
/// auto-route to this SSO provider. ProviderUrl is the IdP's metadata/discovery URL.
/// </summary>
public class SSOClient : BaseEntity
{
    public int WorkspaceId { get; set; }
    public string? Domain { get; set; }
    public string? ProviderUrl { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    public Workspace Workspace { get; set; } = null!;
}
