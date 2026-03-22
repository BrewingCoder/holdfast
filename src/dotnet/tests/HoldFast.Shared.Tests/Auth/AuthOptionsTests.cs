using HoldFast.Shared.Auth;
using Xunit;

namespace HoldFast.Shared.Tests.Auth;

/// <summary>
/// Tests for AuthOptions defaults and configuration.
/// </summary>
public class AuthOptionsTests
{
    [Fact]
    public void DefaultMode_IsPassword()
    {
        var options = new AuthOptions();
        Assert.Equal("Password", options.Mode);
    }

    [Fact]
    public void DefaultJwtSecret_IsDevSecret()
    {
        var options = new AuthOptions();
        Assert.Contains("holdfast-dev-secret", options.JwtSecret);
        Assert.True(options.JwtSecret.Length >= 32, "JWT secret must be at least 32 bytes");
    }

    [Fact]
    public void DefaultTokenExpiry_Is7Days()
    {
        var options = new AuthOptions();
        Assert.Equal(TimeSpan.FromDays(7), options.TokenExpiry);
    }

    [Fact]
    public void DefaultAdminPassword_IsNull()
    {
        var options = new AuthOptions();
        Assert.Null(options.AdminPassword);
    }

    [Fact]
    public void DefaultTokenCookieName_IsToken()
    {
        var options = new AuthOptions();
        Assert.Equal("token", options.TokenCookieName);
    }

    [Fact]
    public void DefaultDemoProjectId_IsNull()
    {
        var options = new AuthOptions();
        Assert.Null(options.DemoProjectId);
    }

    [Fact]
    public void AllFieldsConfigurable()
    {
        var options = new AuthOptions
        {
            Mode = "Simple",
            JwtSecret = "custom-secret-that-is-at-least-32-bytes-long!",
            TokenExpiry = TimeSpan.FromHours(1),
            AdminPassword = "supersecret",
            TokenCookieName = "my-token",
            DemoProjectId = 42,
        };

        Assert.Equal("Simple", options.Mode);
        Assert.Equal("custom-secret-that-is-at-least-32-bytes-long!", options.JwtSecret);
        Assert.Equal(TimeSpan.FromHours(1), options.TokenExpiry);
        Assert.Equal("supersecret", options.AdminPassword);
        Assert.Equal("my-token", options.TokenCookieName);
        Assert.Equal(42, options.DemoProjectId);
    }

    [Theory]
    [InlineData("Simple")]
    [InlineData("Password")]
    [InlineData("Firebase")]
    [InlineData("OAuth")]
    public void Mode_AcceptsAllSupportedValues(string mode)
    {
        var options = new AuthOptions { Mode = mode };
        Assert.Equal(mode, options.Mode);
    }

    [Fact]
    public void TokenExpiry_CanBeZero()
    {
        var options = new AuthOptions { TokenExpiry = TimeSpan.Zero };
        Assert.Equal(TimeSpan.Zero, options.TokenExpiry);
    }

    [Fact]
    public void TokenExpiry_CanBeNegative_ForTestingExpiredTokens()
    {
        var options = new AuthOptions { TokenExpiry = TimeSpan.FromMinutes(-5) };
        Assert.True(options.TokenExpiry < TimeSpan.Zero);
    }

    [Fact]
    public void AuthErrors_AuthenticationError_Is401()
    {
        Assert.IsType<UnauthorizedAccessException>(AuthErrors.AuthenticationError);
        Assert.Contains("401", AuthErrors.AuthenticationError.Message);
    }

    [Fact]
    public void AuthErrors_AuthorizationError_Is403()
    {
        Assert.IsType<UnauthorizedAccessException>(AuthErrors.AuthorizationError);
        Assert.Contains("403", AuthErrors.AuthorizationError.Message);
    }

    [Fact]
    public void AuthErrors_AreSingletonInstances()
    {
        // Static readonly fields are same instance
        Assert.Same(AuthErrors.AuthenticationError, AuthErrors.AuthenticationError);
        Assert.Same(AuthErrors.AuthorizationError, AuthErrors.AuthorizationError);
    }

    [Fact]
    public void AuthErrors_AreDifferentInstances()
    {
        Assert.NotSame(AuthErrors.AuthenticationError, AuthErrors.AuthorizationError);
    }
}
