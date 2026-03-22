using HoldFast.Shared.Auth;

namespace HoldFast.Shared.Tests.Auth;

public class AuthErrorsTests
{
    [Fact]
    public void AuthenticationError_Is401()
    {
        Assert.Contains("401", AuthErrors.AuthenticationError.Message);
    }

    [Fact]
    public void AuthorizationError_Is403()
    {
        Assert.Contains("403", AuthErrors.AuthorizationError.Message);
    }

    [Fact]
    public void AuthenticationError_IsUnauthorizedAccessException()
    {
        Assert.IsType<UnauthorizedAccessException>(AuthErrors.AuthenticationError);
    }

    [Fact]
    public void AuthorizationError_IsUnauthorizedAccessException()
    {
        Assert.IsType<UnauthorizedAccessException>(AuthErrors.AuthorizationError);
    }

    [Fact]
    public void Errors_AreSingletonInstances()
    {
        // Same reference each time
        Assert.Same(AuthErrors.AuthenticationError, AuthErrors.AuthenticationError);
        Assert.Same(AuthErrors.AuthorizationError, AuthErrors.AuthorizationError);
    }

    [Fact]
    public void Errors_AreDifferentInstances()
    {
        Assert.NotSame(AuthErrors.AuthenticationError, AuthErrors.AuthorizationError);
    }

    [Fact]
    public void ClaimTypes_AreCorrectStrings()
    {
        Assert.Equal("uid", HoldFastClaimTypes.Uid);
        Assert.Equal("email", HoldFastClaimTypes.Email);
        Assert.Equal("admin_id", HoldFastClaimTypes.AdminId);
    }
}
