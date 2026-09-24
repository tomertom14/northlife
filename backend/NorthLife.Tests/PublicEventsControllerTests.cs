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

    [Fact]
    public async Task Extreme_date_range_returns_problem_details_instead_of_a_server_error()
    {
        var response = await _client.GetAsync("/api/events/from-to?from=0001-01-01&to=9999-12-31");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemPayload>();
        Assert.NotNull(problem);
        Assert.Equal("invalid_event_query", problem.Code);
    }

    [Fact]
    public async Task Top_picks_honour_the_requested_period()
    {
        var response = await _client.GetAsync("/api/events/top-picks?period=someday");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemPayload>();
        Assert.NotNull(problem);
        Assert.True(problem.Errors.ContainsKey("period"));
    }

    [Fact]
    public async Task Unknown_api_path_is_a_problem_details_404_not_the_web_app()
    {
        var response = await _client.GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private sealed record ValidationProblemPayload(
        string Code,
        Dictionary<string, string[]> Errors);
}
