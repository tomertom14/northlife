using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NorthLife.Api.Authentication;
using NorthLife.Api.Models;

namespace NorthLife.Tests;

/// <summary>Place endpoint behaviour decided before any database access (the test host has no database).</summary>
public sealed class PlaceEndpointTests(NorthLifeApiFactory factory) : IClassFixture<NorthLifeApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Theory]
    [InlineData("/api/places?sort=near", "latitude")]
    [InlineData("/api/places?sort=distance", "sort")]
    [InlineData("/api/places?pageSize=51", "pageSize")]
    [InlineData("/api/places?latitude=33.2", "latitude")]
    [InlineData("/api/places/map?north=32&south=33", "bounds")]
    public async Task Invalid_directory_queries_are_rejected_with_the_field_named(string url, string field)
    {
        var response = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("invalid_place_query", json.RootElement.GetProperty("code").GetString());
        Assert.True(json.RootElement.GetProperty("errors").TryGetProperty(field, out _));
    }

    [Fact]
    public async Task Owner_place_endpoints_need_a_business_owner()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/manage/places")).StatusCode);

        var asAdmin = new HttpRequestMessage(HttpMethod.Get, "/api/manage/places");
        asAdmin.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(UserRole.Admin, mfaVerified: true));
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(asAdmin)).StatusCode);
    }

    [Fact]
    public async Task Place_moderation_needs_an_administrator_who_passed_totp()
    {
        var owner = new HttpRequestMessage(HttpMethod.Get, "/api/admin/places");
        owner.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(UserRole.BusinessOwner, mfaVerified: true));
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(owner)).StatusCode);

        var adminWithoutTotp = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/places/{Guid.NewGuid()}/approve")
        {
            Content = JsonContent.Create(new { revision = 1 }),
        };
        adminWithoutTotp.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(UserRole.Admin, mfaVerified: false));
        var response = await _client.SendAsync(adminWithoutTotp);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("mfa_required", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_invalid_place_is_rejected_before_anything_is_saved()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/manage/places")
        {
            Content = JsonContent.Create(new
            {
                name = "",
                category = "Cafe",
                description = "",
                locality = "תל חי",
                address = "",
                latitude = 33.2,
                longitude = 35.5,
                website = "ftp://example.com",
                imageId = Guid.Empty,
                hours = new[] { new { day = 1, opens = 600, closes = 600 } },
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(UserRole.BusinessOwner, mfaVerified: false));

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("invalid_place", json.RootElement.GetProperty("code").GetString());
        var errors = json.RootElement.GetProperty("errors");
        foreach (var field in new[] { "name", "description", "address", "website", "imageId", "hours" })
        {
            Assert.True(errors.TryGetProperty(field, out _), $"missing error for {field}");
        }
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
}
