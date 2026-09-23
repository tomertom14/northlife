using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NorthLife.Api.Authentication;
using NorthLife.Api.Contracts;
using NorthLife.Api.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace NorthLife.Tests;

public sealed class AuthenticationTests
{
    private const string SigningKey = "northlife-tests-only-signing-key-32-bytes";

    [Fact]
    public void Registration_rejects_weak_passwords()
    {
        var request = new RegisterRequest(
            "Test Owner",
            "owner@example.com",
            "weak",
            "0501234567",
            "Test Business");

        var exception = Assert.Throws<AuthValidationException>(
            () => AuthInputValidator.ValidateRegistration(request));

        Assert.Contains("password", exception.Errors.Keys);
    }

    [Fact]
    public void Passwords_are_hashed_and_verifiable()
    {
        var user = User();
        var hasher = new PasswordHasher<AppUser>();

        var hash = hasher.HashPassword(user, "StrongPass123");

        Assert.NotEqual("StrongPass123", hash);
        Assert.Equal(
            PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(user, hash, "StrongPass123"));
    }

    [Fact]
    public void Jwt_contains_identity_and_business_owner_role_for_sixty_minutes()
    {
        var now = new DateTimeOffset(2026, 9, 23, 8, 0, 0, TimeSpan.Zero);
        var service = TokenService(now);

        var response = service.Create(User());

        Assert.Equal(now.AddMinutes(60), response.ExpiresAt);
        Assert.Equal(UserRole.BusinessOwner, response.User.Role);

        var principal = Validate(response.Token, now.AddMinutes(1));
        Assert.Equal(UserRole.BusinessOwner.ToString(), principal.FindFirstValue(ClaimTypes.Role));
        Assert.Equal("Test Business", principal.FindFirstValue(AuthTokenService.BusinessNameClaim));
    }

    [Fact]
    public void Expired_jwt_is_rejected_without_clock_skew()
    {
        var issuedAt = DateTimeOffset.UtcNow.AddHours(-2);
        var response = TokenService(issuedAt).Create(User());

        Assert.Throws<SecurityTokenInvalidLifetimeException>(
            () => Validate(response.Token, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Registration_contract_has_no_role_input()
    {
        Assert.DoesNotContain(
            typeof(RegisterRequest).GetProperties(),
            property => property.Name.Equals("Role", StringComparison.OrdinalIgnoreCase));
    }

    private static AuthTokenService TokenService(DateTimeOffset now) =>
        new(
            Options.Create(new JwtOptions
            {
                JwtKey = SigningKey,
                Issuer = "NorthLife",
                Audience = "NorthLife.Web",
                TokenLifetimeMinutes = 60,
            }),
            new FixedTimeProvider(now));

    private static ClaimsPrincipal Validate(string token, DateTimeOffset now)
    {
        var handler = new JwtSecurityTokenHandler();
        return handler.ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "NorthLife",
            ValidateAudience = true,
            ValidAudience = "NorthLife.Web",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            LifetimeValidator = (notBefore, expires, _, parameters) =>
                expires is not null &&
                notBefore is not null &&
                notBefore <= now.UtcDateTime &&
                expires > now.UtcDateTime,
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role,
        }, out _);
    }

    private static AppUser User() => new()
    {
        Id = Guid.Parse("019924c0-0000-7000-8000-000000000100"),
        FullName = "Test Owner",
        Email = "owner@example.com",
        NormalizedEmail = "OWNER@EXAMPLE.COM",
        PasswordHash = "unused",
        Phone = "0501234567",
        BusinessName = "Test Business",
        Role = UserRole.BusinessOwner,
        CreatedAtUtc = DateTimeOffset.UtcNow,
    };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
