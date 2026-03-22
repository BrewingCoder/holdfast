using HoldFast.Domain.Entities;
using HoldFast.Shared.Auth;
using Microsoft.Extensions.Options;

namespace HoldFast.Shared.Tests.Auth;

public class SimpleAuthServiceTests
{
    private static SimpleAuthService CreateService() =>
        new(Options.Create(new AuthOptions { Mode = "Simple" }));

    [Fact]
    public void GenerateToken_ReturnsFixedDemoToken()
    {
        var svc = CreateService();
        var token = svc.GenerateToken(new Admin { Id = 1 });
        Assert.Equal("simple-demo-token", token);
    }

    [Fact]
    public void GenerateToken_IgnoresAdminDetails()
    {
        var svc = CreateService();
        var t1 = svc.GenerateToken(new Admin { Id = 1, Uid = "a" });
        var t2 = svc.GenerateToken(new Admin { Id = 99, Uid = "b" });
        Assert.Equal(t1, t2);
    }

    [Fact]
    public void ValidateToken_AnyNonEmptyToken_ReturnsAuthenticatedPrincipal()
    {
        var svc = CreateService();
        var principal = svc.ValidateToken("anything-goes");
        Assert.NotNull(principal);
        Assert.True(principal.Identity?.IsAuthenticated);
    }

    [Fact]
    public void ValidateToken_DemoToken_ReturnsDemoUid()
    {
        var svc = CreateService();
        var principal = svc.ValidateToken("simple-demo-token")!;
        Assert.Equal("demo", svc.GetUid(principal));
    }

    [Fact]
    public void ValidateToken_DemoToken_ReturnsDemoEmail()
    {
        var svc = CreateService();
        var principal = svc.ValidateToken("simple-demo-token")!;
        Assert.Equal("demo@example.com", svc.GetEmail(principal));
    }

    [Fact]
    public void ValidateToken_EmptyString_ReturnsNull()
    {
        var svc = CreateService();
        Assert.Null(svc.ValidateToken(""));
    }

    [Fact]
    public void ValidateToken_Whitespace_ReturnsNull()
    {
        var svc = CreateService();
        Assert.Null(svc.ValidateToken("   "));
    }

    [Fact]
    public void ValidateToken_Null_ReturnsNull()
    {
        var svc = CreateService();
        Assert.Null(svc.ValidateToken(null!));
    }

    [Fact]
    public void ValidateToken_RandomToken_StillReturnsDemoClaims()
    {
        var svc = CreateService();
        var principal = svc.ValidateToken("some-random-token-12345")!;
        Assert.Equal("demo", svc.GetUid(principal));
        Assert.Equal("demo@example.com", svc.GetEmail(principal));
    }

    [Fact]
    public void ValidateToken_AdminIdClaimIsAlways1()
    {
        var svc = CreateService();
        var principal = svc.ValidateToken("any-token")!;
        var adminId = principal.FindFirst(HoldFastClaimTypes.AdminId);
        Assert.NotNull(adminId);
        Assert.Equal("1", adminId.Value);
    }

    [Fact]
    public void GetUid_FallsToDemoUid_WhenNoUidClaim()
    {
        var svc = CreateService();
        var identity = new System.Security.Claims.ClaimsIdentity();
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);
        Assert.Equal("demo", svc.GetUid(principal));
    }

    [Fact]
    public void GetEmail_FallsToDemoEmail_WhenNoEmailClaim()
    {
        var svc = CreateService();
        var identity = new System.Security.Claims.ClaimsIdentity();
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);
        Assert.Equal("demo@example.com", svc.GetEmail(principal));
    }
}
