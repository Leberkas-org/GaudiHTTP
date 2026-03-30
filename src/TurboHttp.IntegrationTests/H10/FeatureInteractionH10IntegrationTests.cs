using System.Net;
using System.Text.Json;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.Protocol.RFC9110;
using TurboHttp.Protocol.RFC9111;

namespace TurboHttp.IntegrationTests.H10;

[Collection("H10")]
public sealed class FeatureInteractionH10IntegrationTests
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    public FeatureInteractionH10IntegrationTests(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    [Fact(DisplayName = "Interaction-H10-001: Redirect preserves cookies across hops")]
    public async Task Redirect_Preserves_Cookies_Across_Hops()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 0),
            configure: builder => builder.WithCookies().WithRedirect(),
            system: _systemFixture.System);

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cookie/set-and-redirect"), cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(cts.Token);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal("from-redirect", cookies["redirect_cookie"]);
    }

    [Fact(DisplayName = "Interaction-H10-002: Compressed response served from cache")]
    public async Task Compressed_Response_Served_From_Cache()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 0),
            configure: builder => builder.WithCache(CachePolicy.Default).WithDecompression(),
            system: _systemFixture.System);

        var req1 = new HttpRequestMessage(HttpMethod.Get, "/interaction/cache-gzip");
        var req2 = new HttpRequestMessage(HttpMethod.Get, "/interaction/cache-gzip");

        var res1 = await helper.Client.SendAsync(req1, cts.Token);
        var res2 = await helper.Client.SendAsync(req2, cts.Token);

        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);

        var body1 = await res1.Content.ReadAsStringAsync(cts.Token);
        var body2 = await res2.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal(body1, body2);
    }

    [Fact(DisplayName = "Interaction-H10-003: Retry after redirect target returns 503")]
    public async Task Retry_After_Redirect_Target_Returns_503()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 0),
            configure: builder => builder.WithRedirect().WithRetry(new RetryPolicy { MaxRetries = 3 }),
            system: _systemFixture.System);

        var key = Guid.NewGuid().ToString("N");
        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/interaction/redirect-succeed-after/2/{key}"), cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("success", body);
    }

    [Fact(DisplayName = "Interaction-H10-004: Cookie survives retry cycle")]
    public async Task Cookie_Survives_Retry_Cycle()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 0),
            configure: builder => builder.WithCookies().WithRetry(new RetryPolicy { MaxRetries = 3 }),
            system: _systemFixture.System);

        var setReq = new HttpRequestMessage(HttpMethod.Get, "/cookie/set/auth-token/abc123");
        await helper.Client.SendAsync(setReq, cts.Token);

        var key = Guid.NewGuid().ToString("N");
        var retryReq = new HttpRequestMessage(HttpMethod.Get, $"/retry/succeed-after/2?key={key}");
        var retryRes = await helper.Client.SendAsync(retryReq, cts.Token);
        Assert.Equal(HttpStatusCode.OK, retryRes.StatusCode);

        var echoReq = new HttpRequestMessage(HttpMethod.Get, "/cookie/echo");
        var echoRes = await helper.Client.SendAsync(echoReq, cts.Token);
        Assert.Equal(HttpStatusCode.OK, echoRes.StatusCode);

        var json = await echoRes.Content.ReadAsStringAsync(cts.Token);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal("abc123", cookies["auth-token"]);
    }

    [Fact(DisplayName = "Interaction-H10-005: Vary header separates cache entries with cookies active")]
    public async Task Vary_Header_Separates_Cache_Entries_With_Cookies_Active()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 0),
            configure: builder => builder.WithCache(CachePolicy.Default).WithCookies(),
            system: _systemFixture.System);

        var req1 = new HttpRequestMessage(HttpMethod.Get, "/cache/vary/Accept-Language");
        req1.Headers.Add("Accept-Language", "en");

        var req2 = new HttpRequestMessage(HttpMethod.Get, "/cache/vary/Accept-Language");
        req2.Headers.Add("Accept-Language", "de");

        var req3 = new HttpRequestMessage(HttpMethod.Get, "/cache/vary/Accept-Language");
        req3.Headers.Add("Accept-Language", "en");

        var res1 = await helper.Client.SendAsync(req1, cts.Token);
        var res2 = await helper.Client.SendAsync(req2, cts.Token);
        var res3 = await helper.Client.SendAsync(req3, cts.Token);

        var body1 = await res1.Content.ReadAsStringAsync(cts.Token);
        var body2 = await res2.Content.ReadAsStringAsync(cts.Token);
        var body3 = await res3.Content.ReadAsStringAsync(cts.Token);

        Assert.Equal(body1, body3);
        Assert.NotEqual(body1, body2);
    }

    [Fact(DisplayName = "Interaction-H10-006: Redirect chain accumulates cookies across hops")]
    public async Task Redirect_Chain_Accumulates_Cookies_Across_Hops()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 0),
            configure: builder => builder.WithCookies().WithRedirect(),
            system: _systemFixture.System);

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/interaction/cookie-hop/1"), cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(cts.Token);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal("val1", cookies["hop1"]);
        Assert.Equal("val2", cookies["hop2"]);
        Assert.Equal("val3", cookies["hop3"]);
    }

    [Fact(DisplayName = "Interaction-H10-007: Cache hit bypasses retry logic")]
    public async Task Cache_Hit_Bypasses_Retry_Logic()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 0),
            configure: builder => builder.WithCache(CachePolicy.Default).WithRetry(new RetryPolicy { MaxRetries = 3 }),
            system: _systemFixture.System);

        var res1 = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cache/max-age/3600"), cts.Token);
        var res2 = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cache/max-age/3600"), cts.Token);

        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);

        var body1 = await res1.Content.ReadAsStringAsync(cts.Token);
        var body2 = await res2.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal(body1, body2);
    }
}
