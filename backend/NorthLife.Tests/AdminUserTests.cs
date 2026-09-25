using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NorthLife.Api.Authentication;
using NorthLife.Api.Models;
using NorthLife.Api.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;

namespace NorthLife.Tests;

public sealed class AdminUserRulesTests
{
    private static readonly Guid Actor = Guid.CreateVersion7();

    private static AppUser User(UserRole role, bool suspended = false, Guid? id = null) => new()
    {
        Id = id ?? Guid.CreateVersion7(),
        FullName = "User",
        Email = "u@example.com",
        NormalizedEmail = "U@EXAMPLE.COM",
        PasswordHash = "x",
        Phone = "0500000000",
        BusinessName = "Biz",
        Role = role,
        SuspendedAtUtc = suspended ? DateTimeOffset.UtcNow : null,
    };

    [Fact]
    public void An_admin_cannot_suspend_or_re_role_themself()
    {
        var self = User(UserRole.Admin, id: Actor);

        Assert.Equal("cannot_suspend_self", AdminUserRules.SuspendProblem(Actor, self, activeAdmins: 3));
        Assert.Equal("cannot_change_own_role", AdminUserRules.RoleChangeProblem(Actor, self, UserRole.BusinessOwner, activeAdmins: 3));
    }

    [Fact]
    public void The_last_active_admin_can_be_neither_suspended_nor_demoted()
    {
        var onlyAdmin = User(UserRole.Admin);

        Assert.Equal("last_admin", AdminUserRules.SuspendProblem(Actor, onlyAdmin, activeAdmins: 1));
        Assert.Equal("last_admin", AdminUserRules.RoleChangeProblem(Actor, onlyAdmin, UserRole.BusinessOwner, activeAdmins: 1));
        Assert.Null(AdminUserRules.RoleChangeProblem(Actor, onlyAdmin, UserRole.BusinessOwner, activeAdmins: 2));
    }

    [Fact]
    public void A_suspended_admin_does_not_count_so_demoting_them_is_allowed()
    {
        Assert.Null(AdminUserRules.RoleChangeProblem(Actor, User(UserRole.Admin, suspended: true), UserRole.BusinessOwner, activeAdmins: 1));
    }

    [Fact]
    public void Business_owners_can_be_suspended_and_promoted()
    {
        var owner = User(UserRole.BusinessOwner);

        Assert.Null(AdminUserRules.SuspendProblem(Actor, owner, activeAdmins: 1));
        Assert.Null(AdminUserRules.RoleChangeProblem(Actor, owner, UserRole.Admin, activeAdmins: 1));
        Assert.Equal("no_change", AdminUserRules.RoleChangeProblem(Actor, owner, UserRole.BusinessOwner, activeAdmins: 1));
        Assert.Equal("already_suspended", AdminUserRules.SuspendProblem(Actor, User(UserRole.BusinessOwner, suspended: true), activeAdmins: 1));
    }
}

public sealed class KeysetCursorTests
{
    [Fact]
    public void Round_trips_position_exactly()
    {
        var position = new KeysetCursor(new DateTimeOffset(2026, 9, 25, 10, 31, 49, TimeSpan.Zero).AddTicks(1230), Guid.CreateVersion7());

        Assert.Equal(position, KeysetCursor.Decode(position.Encode()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64 !")]
    [InlineData("MTIzNDU2")]
    public void Rejects_garbage_instead_of_throwing(string? cursor)
    {
        Assert.Null(KeysetCursor.Decode(cursor));
    }
}

/// <summary>Stateless JWTs become revocable through the security stamp check.</summary>
public sealed class SessionRevocationTests(NorthLifeApiFactory factory) : IClassFixture<NorthLifeApiFactory>
{
    private const string Key = "northlife-tests-only-signing-key-32-bytes";
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task A_revoked_session_is_rejected_even_though_its_signature_and_expiry_are_valid()
    {
        var user = NewUser();
        factory.RevokedUsers.Add(user.Id);

        var response = await SendAsync("/api/auth/security", TokenService().Create(user).Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_without_a_security_stamp_is_rejected()
    {
        var user = NewUser();
        var handler = new JwtSecurityTokenHandler();
        var token = handler.WriteToken(new JwtSecurityToken(
            "NorthLife",
            "NorthLife.Web",
            [new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()), new Claim(ClaimTypes.Role, "Admin"), new Claim(AuthTokenService.MfaClaim, "true")],
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(30),
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256)));

        var response = await SendAsync("/api/admin/users", token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task User_administration_requires_a_verified_admin_session()
    {
        var owner = NewUser();
        var response = await SendAsync("/api/admin/users", TokenService().Create(owner, mfaVerified: true).Token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private Task<HttpResponseMessage> SendAsync(string path, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
    }

    private static AuthTokenService TokenService() =>
        new(Options.Create(new JwtOptions { JwtKey = Key }), TimeProvider.System);

    private static AppUser NewUser() => new()
    {
        FullName = "Owner",
        Email = "o@example.com",
        NormalizedEmail = "O@EXAMPLE.COM",
        PasswordHash = "x",
        Phone = "0500000000",
        BusinessName = "Biz",
    };
}
