using System.Net;
using GaudiHTTP.IntegrationTests.Server.Shared;

namespace GaudiHTTP.IntegrationTests.Server.Middleware;

public sealed class MiddlewareSpec(GaudiServerFixture server) : IDisposable
{
    private readonly HttpClient _client = GaudiServerFixture.CreateClient();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => _client.Dispose();

    [Fact(Timeout = 15000)]
    public async Task Global_middleware_should_set_response_header()
    {
        var response = await _client.GetAsync(
            new Uri($"http://127.0.0.1:{server.Port}/hello"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Powered-By"));
        Assert.Equal("GaudiHTTP", response.Headers.GetValues("X-Powered-By").First());
    }

    [Fact(Timeout = 15000)]
    public async Task Mapped_middleware_should_apply_to_matching_path()
    {
        var response = await _client.GetAsync(
            new Uri($"http://127.0.0.1:{server.Port}/api/data"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Api-Version"));
        Assert.Equal("2.0", response.Headers.GetValues("X-Api-Version").First());
    }

    [Fact(Timeout = 15000)]
    public async Task Mapped_middleware_should_not_apply_to_other_paths()
    {
        var response = await _client.GetAsync(
            new Uri($"http://127.0.0.1:{server.Port}/other"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("X-Api-Version"));
    }

    [Fact(Timeout = 15000)]
    public async Task Global_middleware_should_apply_to_all_paths()
    {
        var response = await _client.GetAsync(
            new Uri($"http://127.0.0.1:{server.Port}/other"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Powered-By"));
        Assert.Equal("GaudiHTTP", response.Headers.GetValues("X-Powered-By").First());
    }
}
