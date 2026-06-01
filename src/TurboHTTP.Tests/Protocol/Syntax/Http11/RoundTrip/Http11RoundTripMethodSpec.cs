using System.Net;
using System.Text;
using Akka.Actor;
using TurboHTTP.Protocol.Syntax;
using TurboHTTP.Protocol.Syntax.Http11.Client;
using TurboHTTP.Protocol.Syntax.Http11.Options;
using TurboHTTP.Tests.TestSupport;

namespace TurboHTTP.Tests.Protocol.Syntax.Http11.RoundTrip;

public sealed class Http11RoundTripMethodSpec
{
    private static readonly Http11ClientEncoder Encoder = new(ClientOptionDefaults.Http11Encoder());

    private static int EncodeRequest(HttpRequestMessage request, Span<byte> buffer)
    {
        return Encoder.Encode(buffer, request, ActorRefs.Nobody);
    }

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
        var outcome = decoder.Feed(data.Span, false, out _);
        Assert.Equal(DecodeOutcome.Complete, outcome);
        return decoder.GetResponse();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-3")]
    public async Task Http11RoundTrip_should_return_200_when_get_round_trip()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/api");
        var buf = new byte[65536];
        var written = EncodeRequest(request, buf.AsSpan());
        var encoded = Encoding.ASCII.GetString(buf, 0, written);
        Assert.StartsWith("GET /api HTTP/1.1\r\n", encoded);

        var raw = BuildResponse(200, "OK", "hello", ("Content-Length", "5"));
        var response = Decode(raw);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("hello", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-3")]
    public void Http11RoundTrip_should_return_201_created_when_post_json_round_trip()
    {
        const string json = "{\"name\":\"Alice\"}";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/users")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        var buf = new byte[65536];
        var written = EncodeRequest(request, buf.AsSpan());
        var encoded = Encoding.ASCII.GetString(buf, 0, written);
        Assert.Contains("POST /users HTTP/1.1", encoded);
        Assert.Contains("Content-Type: application/json", encoded);

        var raw = BuildResponse(201, "Created", "",
            ("Content-Length", "0"), ("Location", "/users/42"));
        var response = Decode(raw);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Location", out var loc));
        Assert.Equal("/users/42", loc.Single());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-3")]
    public void Http11RoundTrip_should_return_204_no_content_when_put_round_trip()
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "http://example.com/resource/1")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        var buf = new byte[65536];
        var written = EncodeRequest(request, buf.AsSpan());
        var encoded = Encoding.ASCII.GetString(buf, 0, written);
        Assert.Contains("PUT /resource/1 HTTP/1.1", encoded);

        var raw = BuildResponse(204, "No Content", "", ("Content-Length", "0"));
        var response = Decode(raw);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-3")]
    public void Http11RoundTrip_should_return_200_when_delete_round_trip()
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, "http://example.com/resource/5");
        var buf = new byte[65536];
        var written = EncodeRequest(request, buf.AsSpan());
        var encoded = Encoding.ASCII.GetString(buf, 0, written);
        Assert.Contains("DELETE /resource/5 HTTP/1.1", encoded);

        var raw = BuildResponse(200, "OK", "", ("Content-Length", "0"));
        var response = Decode(raw);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-3")]
    public async Task Http11RoundTrip_should_return_200_when_patch_round_trip()
    {
        const string patch = "{\"op\":\"replace\",\"path\":\"/name\",\"value\":\"Bob\"}";
        var request = new HttpRequestMessage(new HttpMethod("PATCH"), "http://example.com/item/3")
        {
            Content = new StringContent(patch, Encoding.UTF8, "application/json-patch+json")
        };
        var buf = new byte[65536];
        var written = EncodeRequest(request, buf.AsSpan());
        var encoded = Encoding.ASCII.GetString(buf, 0, written);
        Assert.Contains("PATCH /item/3 HTTP/1.1", encoded);

        const string responseBody = "{\"id\":3}";
        var raw = BuildResponse(200, "OK", responseBody,
            ("Content-Length", responseBody.Length.ToString()),
            ("Content-Type", "application/json"));
        var response = Decode(raw);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(responseBody, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-3")]
    public void Http11RoundTrip_should_return_content_length_header_when_head_round_trip()
    {
        var request = new HttpRequestMessage(HttpMethod.Head, "http://example.com/resource");
        var buf = new byte[65536];
        var written = EncodeRequest(request, buf.AsSpan());
        var encoded = Encoding.ASCII.GetString(buf, 0, written);
        Assert.StartsWith("HEAD /resource HTTP/1.1", encoded);

        var decoder = new Http11ClientDecoder(ClientOptionDefaults.Http11Decoder());
        var raw = BuildResponse(200, "OK", "",
            ("Content-Length", "0"),
            ("Content-Type", "application/octet-stream"));
        var outcome = decoder.Feed(raw.Span, true, out _);
        Assert.Equal(DecodeOutcome.Complete, outcome);
        var response = decoder.GetResponse();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, response.Content.Headers.ContentLength);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-3")]
    public void Http11RoundTrip_should_return_allow_header_when_options_round_trip()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "http://example.com/resource");
        var buf = new byte[65536];
        var written = EncodeRequest(request, buf.AsSpan());
        var encoded = Encoding.ASCII.GetString(buf, 0, written);
        Assert.Contains("OPTIONS /resource HTTP/1.1", encoded);

        var raw = BuildResponse(200, "OK", "",
            ("Content-Length", "0"),
            ("Allow", "GET, POST, PUT, DELETE, OPTIONS"));
        var response = Decode(raw);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Content.Headers.TryGetValues("Allow", out var allowVals));
        Assert.Contains("GET", string.Join(",", allowVals));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-3")]
    public void Http11RoundTrip_should_encode_query_string_when_request_has_query_string_round_trip()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            "http://example.com/search?q=hello+world&page=1");
        var buf = new byte[65536];
        var written = EncodeRequest(request, buf.AsSpan());
        var encoded = Encoding.ASCII.GetString(buf, 0, written);

        Assert.Contains("GET /search?q=hello+world&page=1 HTTP/1.1", encoded);
    }
}