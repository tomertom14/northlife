using Microsoft.Extensions.Options;
using NorthLife.Api.Authentication;
using NorthLife.Api.Models;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace NorthLife.Tests;

/// <summary>Endpoint behaviour decided before any database access (the test host has no database).</summary>
public sealed class IdentityEndpointTests(NorthLifeApiFactory factory) : IClassFixture<NorthLifeApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Admin_endpoints_require_a_session_that_passed_totp()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/events");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(UserRole.Admin, mfaVerified: false));

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("mfa_required", await Code(response));
    }

    [Fact]
    public async Task Business_owner_tokens_never_reach_admin_endpoints_even_with_mfa()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/events");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(UserRole.BusinessOwner, mfaVerified: true));

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("forbidden", await Code(response));
    }

    [Fact]
    public async Task Tampered_mfa_ticket_is_rejected()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/mfa", new { mfaToken = "forged", code = "123456" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("mfa_expired", await Code(response));
    }

    [Fact]
    public async Task Google_sign_in_is_off_until_a_client_id_is_configured()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/google", new { idToken = "anything" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("google_disabled", await Code(response));
    }

    [Fact]
    public async Task Weak_reset_password_is_rejected_before_the_token_is_spent()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/reset-password", new { token = "x", password = "short" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_password", await Code(response));
    }

    [Fact]
    public async Task Public_configuration_exposes_the_google_client_id_field()
    {
        using var json = JsonDocument.Parse(await _client.GetStringAsync("/api/config/public"));

        Assert.True(json.RootElement.TryGetProperty("googleClientId", out _));
    }

    private static string Token(UserRole role, bool mfaVerified) =>
        new AuthTokenService(
            Options.Create(new JwtOptions { JwtKey = "northlife-tests-only-signing-key-32-bytes" }),
            TimeProvider.System)
        .Create(
            new AppUser
            {
                FullName = "Test",
                Email = "t@example.com",
                NormalizedEmail = "T@EXAMPLE.COM",
                PasswordHash = "x",
                Phone = "0500000000",
                BusinessName = "Biz",
                Role = role,
            },
            mfaVerified)
        .Token;

    private static async Task<string?> Code(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
