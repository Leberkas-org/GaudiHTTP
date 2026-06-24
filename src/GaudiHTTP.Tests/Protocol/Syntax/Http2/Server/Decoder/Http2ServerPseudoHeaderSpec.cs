using TurboHTTP.Protocol.Syntax.Http2;
using TurboHTTP.Protocol.Syntax.Http2.Hpack;
using TurboHTTP.Protocol.Syntax.Http2.Options;
using TurboHTTP.Protocol.Syntax.Http2.Server;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Server.Decoder;

public sealed class Http2ServerPseudoHeaderSpec
{
    private static Http2ServerDecoderOptions DefaultDecoderOptions() => new()
    {
        HeaderTableSize = 16 * 1024,
        MaxConcurrentStreams = 100,
        MaxFieldSectionSize = 64 * 1024,
        MaxHeaderBytes = 32 * 1024,
        MaxHeaderCount = 100,
    };

    private readonly HpackEncoder _encoder = new(useHuffman: false);
    private readonly Http2ServerDecoder _decoder = new(DefaultDecoderOptions());

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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void DecodeHeaders_POST_sets_correct_method()
    {
        var headers = new List<HpackHeader>
        {
            new(":method", "POST"),
            new(":path", "/api/data"),
            new(":scheme", "https"),
            new(":authority", "api.example.com"),
        };

        var encoded = EncodeHeaders(headers);
        var state = BuildStreamState(encoded);

        var feature = _decoder.DecodeHeadersToFeature(streamId: 1, endStream: true, state);

        Assert.NotNull(feature);
        Assert.Equal("POST", feature.Method);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void DecodeHeaders_GET_sets_correct_method()
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
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void DecodeHeaders_URI_built_from_scheme_authority_path()
    {
        var headers = new List<HpackHeader>
        {
            new(":method", "GET"),
            new(":path", "/api/v1/users"),
            new(":scheme", "https"),
            new(":authority", "api.example.com:8443"),
        };

        var encoded = EncodeHeaders(headers);
        var state = BuildStreamState(encoded);

        var feature = _decoder.DecodeHeadersToFeature(streamId: 1, endStream: true, state);

        Assert.NotNull(feature);
        Assert.Equal("/api/v1/users", feature.RawTarget);
        Assert.Equal("https", feature.Scheme);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void DecodeHeaders_all_standard_methods_handled()
    {
        var methods = new[] { "GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS" };

        foreach (var method in methods)
        {
            var headers = new List<HpackHeader>
            {
                new(":method", method),
                new(":path", "/"),
                new(":scheme", "https"),
                new(":authority", "example.com"),
            };

            var encoded = EncodeHeaders(headers);
            var state = BuildStreamState(encoded);

            var feature = _decoder.DecodeHeadersToFeature(streamId: 1, endStream: true, state);

            Assert.NotNull(feature);
            Assert.Equal(method, feature.Method);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void DecodeHeaders_missing_scheme_for_non_CONNECT_throws_HttpProtocolException()
    {
        var headers = new List<HpackHeader>
        {
            new(":method", "GET"),
            new(":path", "/"),
            new(":authority", "example.com"),
        };

        var encoded = EncodeHeaders(headers);
        var state = BuildStreamState(encoded);

        var ex = Assert.Throws<HttpProtocolException>(() =>
            _decoder.DecodeHeadersToFeature(streamId: 1, endStream: true, state));

        Assert.Contains(":scheme", ex.Message);
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