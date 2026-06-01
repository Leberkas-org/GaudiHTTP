using System.Text;
using TurboHTTP.Protocol.Syntax;
using TurboHTTP.Protocol.Syntax.Http11.Client;
using TurboHTTP.Protocol.Syntax.Http11.Options;
using TurboHTTP.Tests.TestSupport;

namespace TurboHTTP.Tests.Protocol.Syntax.Http11.Client;

public sealed class Http11ClientDecoderSpec
{
    private readonly Http11ClientDecoder _decoder = new(ClientOptionDefaults.Http11Decoder());

    [Fact(Timeout = 5000)]
    public void Feed_should_decode_simple_response()
    {
        const string response = "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nHello";
        var bytes = Encoding.ASCII.GetBytes(response);

        var outcome = _decoder.Feed(bytes, requestMethodWasHead: false, out var consumed);

        Assert.Equal(DecodeOutcome.Complete, outcome);
        Assert.Equal(bytes.Length, consumed);

        var msg = _decoder.GetResponse();
        Assert.Equal(200, (int)msg.StatusCode);
        Assert.Equal("OK", msg.ReasonPhrase);
        Assert.Equal(new Version(1, 1), msg.Version);
    }

    [Fact(Timeout = 5000)]
    public void Feed_should_handle_multiple_headers()
    {
        const string response = "HTTP/1.1 200 OK\r\n" +
                                "Content-Type: text/plain\r\n" +
                                "Content-Length: 0\r\n" +
                                "Server: TurboHTTP\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(response);

        var outcome = _decoder.Feed(bytes, requestMethodWasHead: false, out var consumed);

        Assert.Equal(DecodeOutcome.Complete, outcome);
        var msg = _decoder.GetResponse();
        Assert.True(msg.Headers.Contains("Server"));
    }

    [Fact(Timeout = 5000)]
    public void Reset_should_clear_state()
    {
        const string response = "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(response);

        _decoder.Feed(bytes, requestMethodWasHead: false, out _);
        var first = _decoder.GetResponse();

        _decoder.Reset();

        const string emptyResponse = "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\n\r\n";
        var emptyBytes = Encoding.ASCII.GetBytes(emptyResponse);
        _decoder.Feed(emptyBytes, requestMethodWasHead: false, out _);
        var second = _decoder.GetResponse();

        Assert.NotEqual((int)first.StatusCode, (int)second.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-2.2")]
    public void Feed_should_handle_bare_cr_in_status_line()
    {
        var raw = "HTTP/1.1 200\rOK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();
        var decoder = new Http11ClientDecoder(ClientOptionDefaults.Http11Decoder());

        var outcome = decoder.Feed(raw, requestMethodWasHead: false, out _);

        Assert.True(outcome is DecodeOutcome.NeedMore or DecodeOutcome.Complete);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.1")]
    public void Feed_should_treat_http10_with_transfer_encoding_as_faulty()
    {
        var raw = Encoding.ASCII.GetBytes(
            "HTTP/1.0 200 OK\r\n" +
            "Transfer-Encoding: chunked\r\n" +
            "\r\n" +
            "5\r\nhello\r\n0\r\n\r\n");
        var decoder = new Http11ClientDecoder(ClientOptionDefaults.Http11Decoder());

        var ex = Assert.Throws<HttpProtocolException>(() => decoder.Feed(raw, requestMethodWasHead: false, out _));
        Assert.Contains("Transfer-Encoding", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7.1.2")]
    public void Feed_should_not_merge_trailers_into_response_headers()
    {
        var raw = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\n" +
            "Transfer-Encoding: chunked\r\n" +
            "\r\n" +
            "5\r\nhello\r\n" +
            "0\r\n" +
            "X-Checksum: abc123\r\n" +
            "\r\n");
        var decoder = new Http11ClientDecoder(ClientOptionDefaults.Http11Decoder());

        var outcome = decoder.Feed(raw, requestMethodWasHead: false, out _);

        Assert.Equal(DecodeOutcome.Complete, outcome);
        var resp = decoder.GetResponse();

        Assert.False(resp.Headers.Contains("X-Checksum"));
        Assert.True(resp.TrailingHeaders.Contains("X-Checksum"));
    }
}