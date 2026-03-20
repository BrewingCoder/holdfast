namespace HoldFast.Shared.Auth;

/// <summary>
/// Configuration for authentication.
/// Maps to the "Auth" section in appsettings.json.
/// </summary>
public class AuthOptions
{
    /// <summary>
    /// Auth mode: "Simple" (dev/demo), "Password" (self-hosted default), "Firebase", "OAuth".
    /// </summary>
    public string Mode { get; set; } = "Password";

    /// <summary>
    /// Secret used to sign/verify JWT tokens (Password mode).
    /// </summary>
    public string JwtSecret { get; set; } = "holdfast-dev-secret-change-me-minimum-32-bytes";

    /// <summary>
    /// JWT token expiry. Default: 7 days.
    /// </summary>
    public TimeSpan TokenExpiry { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Admin password for Password auth mode.
    /// </summary>
    public string? AdminPassword { get; set; }

    /// <summary>
    /// Name of the cookie/header carrying the auth token.
    /// </summary>
    public string TokenCookieName { get; set; } = "token";

    /// <summary>
    /// Demo project ID — allows unauthenticated read access if set.
    /// </summary>
    public int? DemoProjectId { get; set; }
}
