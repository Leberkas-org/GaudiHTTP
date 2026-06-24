using System.Net;
using System.Text;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.AcceptanceTests.H10;

public sealed class ConnectionSpec : ClientAcceptanceTestBase
{
    private static byte[] BuildResponse(string body, HttpStatusCode status = HttpStatusCode.OK,
        string? extraHeaders = null)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.0 {(int)status} {status}\r\n");
        sb.Append($"Content-Length: {Encoding.Latin1.GetByteCount(body)}\r\n");
        if (extraHeaders is not null)
        {
            sb.Append(extraHeaders);
        }

        sb.Append("\r\n");
        sb.Append(body);
        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public async Task Connection_should_close_after_single_request_by_default()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://fake.test/conn/default")
        {
            Version = HttpVersion.Version10
        };

        var response = await SendClientAsync(HttpVersion.Version10, request, (_, _) => BuildResponse("default"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("default", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public async Task Connection_should_allow_sequential_requests_with_keep_alive_opt_in()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://fake.test/conn/keep-alive")
        {
            Version = HttpVersion.Version10
        };

        var response = await SendClientAsync(HttpVersion.Version10, request,
            (_, _) => BuildResponse("keep-alive", extraHeaders: "Connection: Keep-Alive\r\n"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("keep-alive", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.1")]
    public async Task Connection_should_return_expected_body_for_simple_get()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://fake.test/hello")
        {
            Version = HttpVersion.Version10
        };

        var response = await SendClientAsync(HttpVersion.Version10, request, (_, _) => BuildResponse("Hello World"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", body);
    }
}
