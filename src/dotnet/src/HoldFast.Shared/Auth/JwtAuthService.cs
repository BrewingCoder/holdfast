using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HoldFast.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace HoldFast.Shared.Auth;

/// <summary>
/// JWT-based auth service for Password auth mode.
/// Generates and validates HS256 JWT tokens, matching Go's authenticateToken().
/// </summary>
public class JwtAuthService : IAuthService
{
    private readonly AuthOptions _options;
    private readonly SigningCredentials _signingCredentials;
    private readonly TokenValidationParameters _validationParameters;

    public JwtAuthService(IOptions<AuthOptions> options)
    {
        _options = options.Value;

        var keyBytes = Encoding.UTF8.GetBytes(_options.JwtSecret);
        var securityKey = new SymmetricSecurityKey(keyBytes);
        _signingCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        _validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = securityKey,
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    }

    public string GenerateToken(Admin admin)
    {
        var claims = new List<Claim>
        {
            new(HoldFastClaimTypes.Uid, admin.Uid ?? ""),
            new(HoldFastClaimTypes.Email, admin.Email ?? ""),
            new(HoldFastClaimTypes.AdminId, admin.Id.ToString()),
            new(JwtRegisteredClaimNames.Iat,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64),
        };

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.Add(_options.TokenExpiry),
            signingCredentials: _signingCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public ClaimsPrincipal? ValidateToken(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            handler.InboundClaimTypeMap.Clear(); // Preserve original claim names
            var principal = handler.ValidateToken(token, _validationParameters, out _);
            return principal;
        }
        catch (SecurityTokenException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public string? GetUid(ClaimsPrincipal principal)
    {
        return principal.FindFirst(HoldFastClaimTypes.Uid)?.Value;
    }

    public string? GetEmail(ClaimsPrincipal principal)
    {
        return principal.FindFirst(HoldFastClaimTypes.Email)?.Value;
    }
}
