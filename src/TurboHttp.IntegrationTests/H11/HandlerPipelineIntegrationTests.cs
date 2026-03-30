using System.Net;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.Protocol.RFC6265;
using TurboHttp.Protocol.RFC9110;

namespace TurboHttp.IntegrationTests.H11;

[Collection("H11")]
public sealed class HandlerPipelineIntegrationTests
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    public HandlerPipelineIntegrationTests(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    private sealed class TestHeaderHandler : TurboHandler
    {
        public override HttpRequestMessage ProcessRequest(HttpRequestMessage request)
        {
            request.Headers.TryAddWithoutValidation("X-Typed-Handler", "active");
            return request;
        }
    }

    [Fact(DisplayName = "Handler-001: UseRequest injects custom header")]
    public async Task UseRequest_Injects_Custom_Header()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 1),
            configure: b => b.UseRequest(req =>
            {
                req.Headers.TryAddWithoutValidation("X-Custom-Injected", "hello");
                return req;
            }),
            system: _systemFixture.System);

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/headers/echo"), cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(
            response.Headers.TryGetValues("X-Custom-Injected", out var vals),
            "X-Custom-Injected header must be echoed back");
        Assert.Equal("hello", string.Join(",", vals));
    }

    [Fact(DisplayName = "Handler-002: UseResponse adds header to response")]
    public async Task UseResponse_Adds_Header_To_Response()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 1),
            configure: b => b.UseResponse((_, res) =>
            {
                res.Headers.TryAddWithoutValidation("X-Handler-Added", "injected");
                return res;
            }),
            system: _systemFixture.System);

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/hello"), cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(
            response.Headers.TryGetValues("X-Handler-Added", out var vals),
            "X-Handler-Added header must be present on the response");
        Assert.Equal("injected", string.Join(",", vals));
    }

    [Fact(DisplayName = "Handler-003: AddHandler typed handler processes request")]
    public async Task AddHandler_Typed_Handler_Processes_Request()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 1),
            configure: b => b.AddHandler<TestHeaderHandler>(),
            system: _systemFixture.System);

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/headers/echo"), cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(
            response.Headers.TryGetValues("X-Typed-Handler", out var vals),
            "X-Typed-Handler header must be echoed back");
        Assert.Equal("active", string.Join(",", vals));
    }

    [Fact(DisplayName = "Handler-004: Multiple handlers execute in registration order")]
    public async Task Multiple_Handlers_Execute_In_Registration_Order()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 1),
            configure: b => b
                .UseRequest(req =>
                {
                    req.Headers.TryAddWithoutValidation("X-First", "1");
                    return req;
                })
                .UseRequest(req =>
                {
                    req.Headers.TryAddWithoutValidation("X-Second", "2");
                    return req;
                }),
            system: _systemFixture.System);

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/headers/echo"), cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-First", out var firstVals), "X-First must be present");
        Assert.True(response.Headers.TryGetValues("X-Second", out var secondVals), "X-Second must be present");
        Assert.Equal("1", string.Join(",", firstVals));
        Assert.Equal("2", string.Join(",", secondVals));
    }

    [Fact(DisplayName = "Handler-005: Handler sees original request on response")]
    public async Task Handler_Sees_Original_Request_On_Response()
    {
        string? capturedOriginalUrl = null;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 1),
            configure: b => b.UseResponse((original, res) =>
            {
                capturedOriginalUrl = original.RequestUri?.PathAndQuery;
                return res;
            }),
            system: _systemFixture.System);

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/hello"), cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/hello", capturedOriginalUrl);
    }

    [Fact(DisplayName = "Handler-006: Handler works with redirect pipeline")]
    public async Task Handler_Works_With_Redirect_Pipeline()
    {
        // Handler injects X-Handler-Redirect → 302 → /headers/echo
        // Redirect stage forwards the original request (with injected headers) to the new URL.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 1),
            configure: b => b
                .UseRequest(req =>
                {
                    req.Headers.TryAddWithoutValidation("X-Handler-Redirect", "present");
                    return req;
                })
                .WithRedirect(),
            system: _systemFixture.System);

        // /redirect/302/headers/echo → 302 → /headers/echo which echoes request headers
        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/redirect/302/headers/echo"), cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(
            response.Headers.TryGetValues("X-Handler-Redirect", out var vals),
            "X-Handler-Redirect must still be present after redirect");
        Assert.Equal("present", string.Join(",", vals));
    }

    [Fact(DisplayName = "Handler-007: Handler works with compression pipeline")]
    public async Task Handler_Works_With_Compression_Pipeline()
    {
        // UseResponse handler receives a response after decompression stage has run.
        // The handler should see a normal (decompressed) response.
        int? capturedContentLength = null;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 1),
            configure: b => b
                .WithDecompression()
                .UseResponse((_, res) =>
                {
                    capturedContentLength = (int?)res.Content.Headers.ContentLength;
                    return res;
                }),
            system: _systemFixture.System);

        // /compress/gzip/1 → gzip-encoded 1 KB body; WithDecompression decompresses it
        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/compress/gzip/1"), cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(cts.Token);
        Assert.Equal(1024, body.Length);
    }

    [Fact(DisplayName = "Handler-008: Handler works with cookie pipeline")]
    public async Task Handler_Works_With_Cookie_Pipeline()
    {
        // UseRequest injects X-From-Handler.
        // WithCookies causes the jar to inject a Cookie header on subsequent requests.
        // /interaction/echo-all-headers echoes X-* headers AND Cookie as X-Received-Cookie.
        var jar = new CookieJar();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 1),
            configure: b => b
                .UseRequest(req =>
                {
                    req.Headers.TryAddWithoutValidation("X-From-Handler", "yes");
                    return req;
                })
                .WithCookies(jar),
            system: _systemFixture.System);

        // Seed the jar with a cookie via the set endpoint
        var seedResponse = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cookie/set/testcookie/testvalue"), cts.Token);
        Assert.Equal(HttpStatusCode.OK, seedResponse.StatusCode);

        // Now both the handler header and the cookie should be present
        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/interaction/echo-all-headers"), cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(
            response.Headers.TryGetValues("X-From-Handler", out var handlerVals),
            "X-From-Handler must be echoed back");
        Assert.Equal("yes", string.Join(",", handlerVals));
        Assert.True(
            response.Headers.TryGetValues("X-Received-Cookie", out var cookieVals),
            "X-Received-Cookie must be present (cookie jar injected cookie)");
        Assert.Contains("testcookie=testvalue", string.Join(",", cookieVals));
    }
}
