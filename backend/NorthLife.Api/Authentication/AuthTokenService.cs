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

    /// <summary>"true" only when this session passed the TOTP or backup-code step.</summary>
    public const string MfaClaim = "northlife:mfa";

    /// <summary>The account's security stamp at issue time; see <see cref="SessionValidator"/>.</summary>
    public const string StampClaim = "northlife:stamp";
    private readonly JwtOptions _options = options.Value;

    public AuthResponse Create(AppUser user, bool mfaVerified = false)
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
            new Claim(MfaClaim, mfaVerified ? "true" : "false"),
            new Claim(StampClaim, user.SecurityStamp),
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
            ToUserResponse(user, mfaVerified));
    }

    public static AuthUserResponse ToUserResponse(AppUser user, bool mfaVerified) =>
        new(
            user.Id,
            user.FullName,
            user.Email,
            user.BusinessName,
            user.Role,
            user.EmailConfirmed,
            user.TotpEnabled,
            mfaVerified);
}
