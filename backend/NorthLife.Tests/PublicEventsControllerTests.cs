using System.Net;
using System.Net.Http.Json;

namespace NorthLife.Tests;

public sealed class PublicEventsControllerTests : IClassFixture<NorthLifeApiFactory>
{
    private readonly HttpClient _client;

    public PublicEventsControllerTests(NorthLifeApiFactory factory)
    {
        _client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
    }

    [Fact]
    public async Task Invalid_page_returns_problem_details_without_querying_database()
    {
        var response = await _client.GetAsync("/api/events/today?page=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemPayload>();
        Assert.NotNull(problem);
        Assert.Equal("invalid_event_query", problem.Code);
        Assert.True(problem.Errors.ContainsKey("page"));
    }

    [Fact]
    public async Task Reversed_range_returns_problem_details_without_querying_database()
    {
        var response = await _client.GetAsync(
            "/api/events/from-to?from=2026-09-24&to=2026-09-22");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemPayload>();
        Assert.NotNull(problem);
        Assert.Equal("invalid_event_query", problem.Code);
        Assert.True(problem.Errors.ContainsKey("to"));
    }

    [Fact]
    public async Task Invalid_map_bounds_return_problem_details_without_querying_database()
    {
        var response = await _client.GetAsync("/api/events/map?south=34&north=33&west=34&east=36");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemPayload>();
        Assert.NotNull(problem);
        Assert.True(problem.Errors.ContainsKey("bounds"));
    }

    private sealed record ValidationProblemPayload(
        string Code,
        Dictionary<string, string[]> Errors);
}
