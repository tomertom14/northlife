using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;
using NorthLife.Api.Authentication;
using NorthLife.Api.Models;

namespace NorthLife.Tests;

/// <summary>The automatic approval endpoints are for administrators who passed two-factor sign-in only.</summary>
public sealed class AutoModerationEndpointTests(NorthLifeApiFactory factory) : IClassFixture<NorthLifeApiFactory>
{
    private const string Key = "northlife-tests-only-signing-key-32-bytes";
    private readonly HttpClient _client = factory.CreateClient();

    public static TheoryData<string, string> Routes => new()
    {
        { "GET", "/api/admin/auto-moderation" },
        { "PUT", "/api/admin/auto-moderation/settings" },
        { "POST", "/api/admin/auto-moderation/run" },
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task A_business_owner_is_refused(string method, string path)
    {
        var response = await SendAsync(method, path, Token(UserRole.BusinessOwner, mfaVerified: true));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task An_admin_without_two_factor_sign_in_is_refused(string method, string path)
    {
        var response = await SendAsync(method, path, Token(UserRole.Admin, mfaVerified: false));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task An_anonymous_caller_must_sign_in(string method, string path)
    {
        var response = await SendAsync(method, path, token: null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private Task<HttpResponseMessage> SendAsync(string method, string path, string? token)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method != "GET") request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
    }

    private static string Token(UserRole role, bool mfaVerified) =>
        new AuthTokenService(Options.Create(new JwtOptions { JwtKey = Key }), TimeProvider.System)
            .Create(
                new AppUser
                {
                    FullName = "User",
                    Email = "u@example.com",
                    NormalizedEmail = "U@EXAMPLE.COM",
                    PasswordHash = "x",
                    Phone = "0500000000",
                    BusinessName = "Biz",
                    Role = role,
                },
                mfaVerified)
            .Token;
}
