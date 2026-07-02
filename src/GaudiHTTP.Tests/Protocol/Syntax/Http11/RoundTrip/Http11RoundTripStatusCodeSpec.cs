using System.Net;
using System.Text;
using GaudiHTTP.Protocol.Syntax;
using GaudiHTTP.Protocol.Syntax.Http11.Client;
using GaudiHTTP.Tests.TestSupport;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http11.RoundTrip;

public sealed class Http11RoundTripStatusCodeSpec
{
    private static ReadOnlyMemory<byte> BuildResponse(int status, string reason, string body,
        params (string Name, string Value)[] headers)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {status} {reason}\r\n");
        foreach (var (name, value) in headers)
        {
            sb.Append($"{name}: {value}\r\n");
        }

        sb.Append("\r\n");
        sb.Append(body);
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static HttpResponseMessage Decode(ReadOnlyMemory<byte> data)
    {
        var decoder = new Http11ClientDecoder(ClientOptionDefaults.Http11Decoder());
        var outcome = decoder.Feed(data, false, out _);
        Assert.Equal(DecodeOutcome.Complete, outcome);
        return decoder.GetResponse();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void Http11RoundTrip_should_return_301_with_location_when_get_round_trip()
    {
        var raw = BuildResponse(301, "Moved Permanently", "",
            ("Content-Length", "0"),
            ("Location", "http://example.com/new-path"));
        var response = Decode(raw);
        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Location", out var loc));
        Assert.Contains("new-path", loc.Single());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public async Task Http11RoundTrip_should_return_404_when_resource_missing_round_trip()
    {
        const string body = "Not Found";
        var raw = BuildResponse(404, "Not Found", body, ("Content-Length", body.Length.ToString()));
        var response = Decode(raw);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Not Found", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void Http11RoundTrip_should_return_500_when_server_error_round_trip()
    {
        var raw = BuildResponse(500, "Internal Server Error", "", ("Content-Length", "0"));
        var response = Decode(raw);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void Http11RoundTrip_should_return_503_with_retry_after_when_service_unavailable_round_trip()
    {
        var raw = BuildResponse(503, "Service Unavailable", "",
            ("Content-Length", "0"),
            ("Retry-After", "120"));
        var response = Decode(raw);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Retry-After", out var retryAfter));
        Assert.Equal("120", retryAfter.Single());
    }
}