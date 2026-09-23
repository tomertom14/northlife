using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NorthLife.Api.Contracts;
using NorthLife.Api.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace NorthLife.Api.Authentication;

public sealed class AuthTokenService(
    IOptions<JwtOptions> options,
    TimeProvider timeProvider)
{
    public const string BusinessNameClaim = "northlife:business_name";
    private readonly JwtOptions _options = options.Value;

    public AuthResponse Create(AppUser user)
    {
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.AddMinutes(_options.TokenLifetimeMinutes);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.Name, user.FullName),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim(BusinessNameClaim, user.BusinessName),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
        };
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.JwtKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            _options.Issuer,
            _options.Audience,
            claims,
            now.UtcDateTime,
            expiresAt.UtcDateTime,
            credentials);

        return new AuthResponse(
            new JwtSecurityTokenHandler().WriteToken(token),
            expiresAt,
            new AuthUserResponse(
                user.Id,
                user.FullName,
                user.Email,
                user.BusinessName,
                user.Role));
    }
}
