using System.Net;
using TurboHTTP.Protocol.Syntax.Http3;
using TurboHTTP.Protocol.Syntax.Http3.Client;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Client.Decoder;

public sealed class Http3ResponseDecoderSpec
{
    private readonly QpackTableSync _tableSync = new();
    private readonly Http3ClientDecoder _decoder;

    public Http3ResponseDecoderSpec()
    {
        _decoder = new Http3ClientDecoder(_tableSync);
    }

    private HeadersFrame EncodeHeaders(params (string Name, string Value)[] headers)
    {
        return new HeadersFrame(_tableSync.Encoder.Encode(headers));
    }

    [Fact(Timeout = 5000)]
    public void DecodeHeaders_should_parse_status_code()
    {
        var state = new StreamState();
        var frame = EncodeHeaders((":status", "200"));

        var result = _decoder.DecodeHeaders(frame, state);

        Assert.True(result);
        Assert.True(state.HasResponse);
        Assert.Equal(HttpStatusCode.OK, state.GetResponse().StatusCode);
    }

    [Fact(Timeout = 5000)]
    public void DecodeHeaders_should_parse_response_headers()
    {
        var state = new StreamState();
        var frame = EncodeHeaders(
            (":status", "200"),
            ("x-custom", "value"),
            ("server", "test"));

        _decoder.DecodeHeaders(frame, state);

        var response = state.GetResponse();
        Assert.Equal("value", response.Headers.GetValues("x-custom").Single());
        Assert.Equal("test", response.Headers.GetValues("server").Single());
    }

    [Fact(Timeout = 5000)]
    public void DecodeHeaders_should_capture_content_headers()
    {
        var state = new StreamState();
        var frame = EncodeHeaders(
            (":status", "200"),
            ("content-type", "text/plain"),
            ("content-length", "42"));

        _decoder.DecodeHeaders(frame, state);

        Assert.True(state.HasContentHeaders);
    }

    [Fact(Timeout = 5000)]
    public void DecodeHeaders_should_skip_trailing_headers()
    {
        var state = new StreamState();
        var first = EncodeHeaders((":status", "200"));
        var trailing = EncodeHeaders(("x-trailer", "value"));

        _decoder.DecodeHeaders(first, state);
        var result = _decoder.DecodeHeaders(trailing, state);

        Assert.False(result);
        Assert.Equal(HttpStatusCode.OK, state.GetResponse().StatusCode);
    }
}