using System.Net;
using System.Text.Json;
using TurboHTTP.IntegrationTests.Server.Shared;
using TurboHTTP.Server;

namespace TurboHTTP.IntegrationTests.Server.Hosting.Tls;

/// <summary>
/// Server advertises only HTTP/1.1. A client that prefers HTTP/2 must gracefully fall back
/// to HTTP/1.1 via ALPN rather than failing the handshake.
/// </summary>
[Collection("Infrastructure")]
public sealed class AlpnFallbackSpec : MultiProtocolTlsServerSpecBase
{
    protected override HttpProtocols ServerProtocols => HttpProtocols.Http1;

    [Fact(Timeout = 15000)]
    public async Task Alpn_should_fall_back_to_http11_when_server_does_not_offer_h2()
    {
        using var client = CreateVersionedTlsClient(HttpVersion.Version20);

        var response = await client.GetAsync(Url("/protocol"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var protocol = JsonSerializer.Deserialize<string>(body);
        Assert.Equal("HTTP/1.1", protocol);
    }
}
