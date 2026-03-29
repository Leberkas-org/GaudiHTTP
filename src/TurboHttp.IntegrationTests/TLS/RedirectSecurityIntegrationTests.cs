using System.Net;
using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.TLS;

[Collection("TLS")]
public sealed class RedirectSecurityIntegrationTests
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    public RedirectSecurityIntegrationTests(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    private ClientHelper CreateHttpsRedirectClient()
    {
        return ClientHelper.CreateClient(
            _server.HttpsPort,
            new Version(1, 1),
            scheme: "https",
            configure: builder => builder.WithRedirect(),
            system: _systemFixture.System);
    }

    // ── HTTPS→HTTP Downgrade ────────────────────────────────────────────────

    [Fact(DisplayName = "RFC9110-15.4-RSI-001: 301 HTTPS→HTTP downgrade returns redirect response (blocked)")]
    public async Task Https_To_Http_301_Downgrade_Blocked()
    {
        // RedirectBidiStage catches ProtocolDowngrade and forwards the final redirect response.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateHttpsRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/cross-scheme/301");
        var response = await helper.Client.SendAsync(request, cts.Token);

        // The stage forwards the 301 response when downgrade is blocked
        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
    }

    [Fact(DisplayName = "RFC9110-15.4-RSI-002: 302 HTTPS→HTTP downgrade returns redirect response (blocked)")]
    public async Task Https_To_Http_302_Downgrade_Blocked()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateHttpsRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/cross-scheme");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
    }
}
