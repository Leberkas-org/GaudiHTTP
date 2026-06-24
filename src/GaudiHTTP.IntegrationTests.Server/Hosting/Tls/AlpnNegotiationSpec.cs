using System.Net;
using System.Text.Json;
using GaudiHTTP.IntegrationTests.Server.Shared;
using GaudiHTTP.Server;

namespace GaudiHTTP.IntegrationTests.Server.Hosting.Tls;

/// <summary>
/// Server advertises both HTTP/1.1 and HTTP/2 over TLS; the negotiated protocol must follow
/// what the client requests via ALPN.
/// </summary>
[Collection("Infrastructure")]
public sealed class AlpnNegotiationSpec : MultiProtocolTlsServerSpecBase
{
    protected override HttpProtocols ServerProtocols => HttpProtocols.Http1AndHttp2;

    private async Task<string> GetNegotiatedProtocol(HttpClient client)
    {
        var response = await client.GetAsync(Url("/protocol"), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        return JsonSerializer.Deserialize<string>(body)!;
    }

    [Fact(Timeout = 15000)]
    public async Task Alpn_should_negotiate_http2_when_client_requests_h2()
    {
        using var client = CreateVersionedTlsClient(HttpVersion.Version20);

        var protocol = await GetNegotiatedProtocol(client);

        Assert.Equal("HTTP/2", protocol);
    }

    [Fact(Timeout = 15000)]
    public async Task Alpn_should_negotiate_http11_when_client_requests_h1_on_multi_protocol_server()
    {
        using var client = CreateVersionedTlsClient(HttpVersion.Version11);

        var protocol = await GetNegotiatedProtocol(client);

        Assert.Equal("HTTP/1.1", protocol);
    }
}
