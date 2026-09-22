using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;

namespace NorthLife.Tests;

public sealed class HealthEndpointsTests : IClassFixture<NorthLifeApiFactory>
{
    private readonly HttpClient _client;

    public HealthEndpointsTests(NorthLifeApiFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
    }

    [Fact]
    public async Task Liveness_does_not_depend_on_database()
    {
        var response = await _client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readiness_reports_unavailable_database()
    {
        var response = await _client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}

public sealed class NorthLifeApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting(
            "ConnectionStrings:Database",
            "Host=127.0.0.1;Port=1;Database=northlife;Username=test;Password=test;Timeout=1;Command Timeout=1");
    }
}
