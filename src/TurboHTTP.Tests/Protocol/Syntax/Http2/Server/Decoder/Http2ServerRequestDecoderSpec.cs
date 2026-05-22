using TurboHTTP.Context.Features;
using TurboHTTP.Protocol.Syntax.Http2;
using TurboHTTP.Protocol.Syntax.Http2.Hpack;
using TurboHTTP.Protocol.Syntax.Http2.Server;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Server.Decoder;

public sealed class Http2ServerRequestDecoderSpec
{
    private readonly HpackEncoder _encoder = new(useHuffman: false);
    private readonly Http2ServerDecoder _decoder = new();

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void DecodeHeaders_GET_with_all_pseudoheaders_returns_correct_method_and_uri()
    {
        var headers = new List<HpackHeader>
        {
            new(":method", "GET"),
            new(":path", "/index.html"),
            new(":scheme", "https"),
            new(":authority", "example.com"),
        };

        var encoded = EncodeHeaders(headers);
        var state = BuildStreamState(encoded);

        var feature = _decoder.DecodeHeadersToFeature(streamId: 1, endStream: true, state);

        Assert.NotNull(feature);
        Assert.Equal("GET", feature.Method);
        Assert.Equal("/index.html", feature.RawTarget);
        Assert.Equal("https", feature.Scheme);
        Assert.Equal("HTTP/2", feature.Protocol);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void DecodeHeaders_POST_with_content_type_includes_content_headers()
    {
        var headers = new List<HpackHeader>
        {
            new(":method", "POST"),
            new(":path", "/api/data"),
            new(":scheme", "https"),
            new(":authority", "api.example.com"),
            new("content-type", "application/json"),
            new("content-length", "42"),
        };

        var encoded = EncodeHeaders(headers);
        var state = BuildStreamState(encoded);

        var feature = _decoder.DecodeHeadersToFeature(streamId: 1, endStream: true, state);

        Assert.NotNull(feature);
        Assert.Equal("POST", feature.Method);
        Assert.True(feature.Headers.ContainsKey("content-type"));
        Assert.True(feature.Headers.ContainsKey("content-length"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void DecodeHeaders_missing_method_throws_HttpProtocolException()
    {
        var headers = new List<HpackHeader>
        {
            new(":path", "/index.html"),
            new(":scheme", "https"),
            new(":authority", "example.com"),
        };

        var encoded = EncodeHeaders(headers);
        var state = BuildStreamState(encoded);

        var ex = Assert.Throws<HttpProtocolException>(() =>
            _decoder.DecodeHeadersToFeature(streamId: 1, endStream: true, state));

        Assert.Contains(":method", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void DecodeHeaders_missing_path_for_non_CONNECT_throws_HttpProtocolException()
    {
        var headers = new List<HpackHeader>
        {
            new(":method", "GET"),
            new(":scheme", "https"),
            new(":authority", "example.com"),
        };

        var encoded = EncodeHeaders(headers);
        var state = BuildStreamState(encoded);

        var ex = Assert.Throws<HttpProtocolException>(() =>
            _decoder.DecodeHeadersToFeature(streamId: 1, endStream: true, state));

        Assert.Contains(":path", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.5")]
    public void DecodeHeaders_CONNECT_without_path_and_scheme_succeeds()
    {
        var headers = new List<HpackHeader>
        {
            new(":method", "CONNECT"),
            new(":authority", "example.com:443"),
        };

        var encoded = EncodeHeaders(headers);
        var state = BuildStreamState(encoded);

        var feature = _decoder.DecodeHeadersToFeature(streamId: 1, endStream: true, state);

        Assert.NotNull(feature);
        Assert.Equal("CONNECT", feature.Method);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void DecodeHeaders_endStream_false_returns_null()
    {
        var headers = new List<HpackHeader>
        {
            new(":method", "POST"),
            new(":path", "/data"),
            new(":scheme", "https"),
            new(":authority", "example.com"),
        };

        var encoded = EncodeHeaders(headers);
        var state = BuildStreamState(encoded);

        var feature = _decoder.DecodeHeadersToFeature(streamId: 1, endStream: false, state);

        Assert.Null(feature);
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void DecodeHeaders_with_regular_headers_includes_them_in_request()
    {
        var headers = new List<HpackHeader>
        {
            new(":method", "GET"),
            new(":path", "/"),
            new(":scheme", "https"),
            new(":authority", "example.com"),
            new("user-agent", "test-client/1.0"),
            new("accept", "application/json"),
        };

        var encoded = EncodeHeaders(headers);
        var state = BuildStreamState(encoded);

        var feature = _decoder.DecodeHeadersToFeature(streamId: 1, endStream: true, state);

        Assert.NotNull(feature);
        Assert.True(feature.Headers.ContainsKey("user-agent"));
        Assert.True(feature.Headers.ContainsKey("accept"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void DecodeHeaders_missing_authority_for_non_CONNECT_throws_HttpProtocolException()
    {
        var headers = new List<HpackHeader>
        {
            new(":method", "GET"),
            new(":path", "/"),
            new(":scheme", "https"),
        };

        var encoded = EncodeHeaders(headers);
        var state = BuildStreamState(encoded);

        var ex = Assert.Throws<HttpProtocolException>(() =>
            _decoder.DecodeHeadersToFeature(streamId: 1, endStream: true, state));

        Assert.Contains(":authority", ex.Message);
    }

    private byte[] EncodeHeaders(List<HpackHeader> headers)
    {
        using var owner = System.Buffers.MemoryPool<byte>.Shared.Rent(4096);
        var span = owner.Memory.Span;
        var bytesWritten = _encoder.Encode(headers, ref span, useHuffman: false);
        return owner.Memory[..bytesWritten].ToArray();
    }

    private static StreamState BuildStreamState(byte[] headerBlock)
    {
        var state = new StreamState();
        state.AppendHeader(headerBlock);
        return state;
    }
}