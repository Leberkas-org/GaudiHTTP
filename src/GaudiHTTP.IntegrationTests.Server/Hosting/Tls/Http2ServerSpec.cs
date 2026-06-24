using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using GaudiHTTP.IntegrationTests.Server.Shared;
using GaudiHTTP.Server;

namespace GaudiHTTP.IntegrationTests.Server.Hosting.Tls;

/// <summary>
/// Real HTTP/2 requests against TurboServer over TLS, driven by a neutral .NET HttpClient.
/// </summary>
[Collection("Infrastructure")]
public sealed class Http2ServerSpec : MultiProtocolTlsServerSpecBase
{
    protected override HttpProtocols ServerProtocols => HttpProtocols.Http1AndHttp2;

    protected override void ConfigureEndpoints(WebApplication app)
    {
        base.ConfigureEndpoints(app);
        app.MapGet("/status/{code:int}", (int code) => Results.StatusCode(code));
        app.MapGet("/id/{id:int}", (int id) => Results.Ok(id));
    }

    [Fact(Timeout = 15000)]
    public async Task Http2_should_echo_post_body_over_h2()
    {
        var payload = new string('x', 4 * 1024);
        var request = NewRequest(HttpMethod.Post, "/echo");
        request.Content = new StringContent(payload);

        var response = await Client.SendAsync(request, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version20, response.Version);
        Assert.Equal(payload, System.Text.Json.JsonSerializer.Deserialize<string>(body));
    }

    [Theory(Timeout = 15000)]
    [InlineData(200)]
    [InlineData(404)]
    [InlineData(500)]
    public async Task Http2_should_return_requested_status_code(int code)
    {
        var response = await Client.GetAsync(Url($"/status/{code}"), CancellationToken);

        Assert.Equal(HttpVersion.Version20, response.Version);
        Assert.Equal(code, (int)response.StatusCode);
    }

    [Fact(Timeout = 20000)]
    public async Task Http2_should_multiplex_concurrent_requests_on_one_connection()
    {
        var tasks = Enumerable.Range(0, 20)
            .Select(async i =>
            {
                var response = await Client.GetAsync(Url($"/id/{i}"), CancellationToken);
                Assert.Equal(HttpVersion.Version20, response.Version);
                var body = await response.Content.ReadAsStringAsync(CancellationToken);
                return (i, value: System.Text.Json.JsonSerializer.Deserialize<int>(body));
            })
            .ToArray();

        var results = await Task.WhenAll(tasks);

        foreach (var (i, value) in results)
        {
            Assert.Equal(i, value);
        }
    }
}
