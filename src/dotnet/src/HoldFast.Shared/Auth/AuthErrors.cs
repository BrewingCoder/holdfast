namespace HoldFast.Shared.Auth;

/// <summary>
/// Standard auth error types, matching Go backend conventions.
/// </summary>
public static class AuthErrors
{
    public static readonly Exception AuthenticationError =
        new UnauthorizedAccessException("401 - AuthenticationError");

    public static readonly Exception AuthorizationError =
        new UnauthorizedAccessException("403 - AuthorizationError");
}
