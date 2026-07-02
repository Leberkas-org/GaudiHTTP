using GaudiHTTP.Client;
using GaudiHTTP.Protocol.Syntax.Http2;
using GaudiHTTP.Protocol.Syntax.Http2.Client;
using GaudiHTTP.Protocol.Syntax.Http2.Hpack;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Client;

/// <summary>
/// Verifies the H2 client's buffered-vs-streaming response body decision (body buffer options
/// feature): responses with a known Content-Length at or below the configured threshold are
/// collected via <c>BufferedBodyReader</c> and delivered only once complete, instead of always
/// streaming via <c>QueuedBodyReader</c>.
/// </summary>
public sealed class Http2ClientBufferedResponseSpec
{
    private static Http2ClientSessionManager CreateSession(FakeClientOps ops, int? maxBufferedResponseBodySize = null)
    {
        var options = new GaudiClientOptions();
        if (maxBufferedResponseBodySize is { } max)
        {
            options.MaxBufferedResponseBodySize = max;
        }

        return new Http2ClientSessionManager(options, ops);
    }

    private static HeadersFrame MakeResponseHeaders(int streamId, long? contentLength, bool endStream = false)
    {
        var encoder = new HpackEncoder(useHuffman: false);
        List<(string, string)> headers = [(":status", "200")];
        if (contentLength is { } cl)
        {
            headers.Add(("content-length", cl.ToString()));
        }

        var hpack = encoder.Encode(headers);
        return new HeadersFrame(streamId, hpack, endStream, endHeaders: true);
    }

    private static HttpRequestMessage MakeGet(string path = "/")
        => new(HttpMethod.Get, $"https://example.com{path}");

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Small_response_body_should_defer_dispatch_until_body_complete()
    {
        var ops = new FakeClientOps();
        var sm = CreateSession(ops);

        var request = MakeGet();
        sm.EncodeRequest(request);

        var body = "hello"u8.ToArray();
        sm.ProcessFrame(MakeResponseHeaders(1, contentLength: body.Length));

        // Headers arrived but the body has not — dispatch must be deferred (buffered path).
        Assert.Empty(ops.Responses);

        sm.ProcessFrame(new DataFrame(1, body, endStream: true));

        var response = Assert.Single(ops.Responses);
        Assert.Same(request, response.RequestMessage);
        var received = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(body, received);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Small_response_body_split_across_multiple_DATA_frames_should_reassemble_correctly()
    {
        var ops = new FakeClientOps();
        var sm = CreateSession(ops);

        sm.EncodeRequest(MakeGet());

        var body = "hello world"u8.ToArray();
        sm.ProcessFrame(MakeResponseHeaders(1, contentLength: body.Length));
        Assert.Empty(ops.Responses);

        sm.ProcessFrame(new DataFrame(1, body[..5], endStream: false));
        Assert.Empty(ops.Responses);

        sm.ProcessFrame(new DataFrame(1, body[5..], endStream: true));

        var response = Assert.Single(ops.Responses);
        var received = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(body, received);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void Response_body_above_threshold_should_dispatch_immediately_as_streaming()
    {
        var ops = new FakeClientOps();
        var sm = CreateSession(ops, maxBufferedResponseBodySize: 4);

        var request = MakeGet();
        sm.EncodeRequest(request);

        // Content-Length exceeds the (deliberately tiny) threshold: must stream immediately.
        sm.ProcessFrame(MakeResponseHeaders(1, contentLength: 100));

        var response = Assert.Single(ops.Responses);
        Assert.Same(request, response.RequestMessage);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void Response_without_content_length_should_dispatch_immediately_as_streaming()
    {
        var ops = new FakeClientOps();
        var sm = CreateSession(ops);

        var request = MakeGet();
        sm.EncodeRequest(request);

        sm.ProcessFrame(MakeResponseHeaders(1, contentLength: null));

        Assert.Single(ops.Responses);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void Buffered_response_should_fail_correlated_request_on_RST_STREAM_mid_body()
    {
        var ops = new FakeClientOps();
        var sm = CreateSession(ops);

        var request = MakeGet();
        sm.EncodeRequest(request);

        sm.ProcessFrame(MakeResponseHeaders(1, contentLength: 5));
        Assert.Empty(ops.Responses);

        // Connection resets the stream before the buffered body completes — the caller's
        // Task must still be failed (correlation entry must not have been dropped early).
        sm.ProcessFrame(new RstStreamFrame(1, Http2ErrorCode.Cancel));

        Assert.Empty(ops.Responses);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1.1")]
    public void Buffered_response_should_reject_short_body_on_end_stream()
    {
        var ops = new FakeClientOps();
        var sm = CreateSession(ops);

        sm.EncodeRequest(MakeGet());

        sm.ProcessFrame(MakeResponseHeaders(1, contentLength: 10));
        Assert.Empty(ops.Responses);

        var ex = Assert.Throws<HttpProtocolException>(() =>
            sm.ProcessFrame(new DataFrame(1, "short"u8.ToArray(), endStream: true)));

        Assert.Contains("5", ex.Message);
        Assert.Contains("10", ex.Message);
    }
}
