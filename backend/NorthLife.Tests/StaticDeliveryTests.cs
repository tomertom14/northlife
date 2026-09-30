using System.Net.Http.Headers;
using NorthLife.Api.Health;

namespace NorthLife.Tests;

public sealed class StaticDeliveryTests(NorthLifeApiFactory factory) : IClassFixture<NorthLifeApiFactory>
{
    [Theory]
    [InlineData("main-SONQTXSQ.js", StaticCaching.Immutable)]
    [InlineData("chunk-Dh9dwTPD.js", StaticCaching.Immutable)]
    [InlineData("styles-Z5BGMJHQ.css", StaticCaching.Immutable)]
    [InlineData("index.html", StaticCaching.Revalidate)]
    [InlineData("favicon.ico", StaticCaching.Short)]
    [InlineData("music.svg", StaticCaching.Short)]
    public void Hashed_build_files_are_immutable_and_the_shell_always_revalidates(string fileName, string expected) =>
        Assert.Equal(expected, StaticCaching.For(fileName));

    [Fact]
    public async Task Json_responses_are_never_compressed()
    {
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/config/public");
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("br"));
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));

        var response = await client.SendAsync(request);

        Assert.True(response.IsSuccessStatusCode);
        Assert.Empty(response.Content.Headers.ContentEncoding);
    }
}
