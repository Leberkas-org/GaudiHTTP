using GaudiHTTP.Protocol.Syntax.Http2;
using GaudiHTTP.Protocol.Syntax.Http2.Hpack;
using GaudiHTTP.Protocol.Syntax.Http2.Options;
using GaudiHTTP.Protocol.Syntax.Http2.Server;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Server.Decoder;

public sealed class Http2ServerDecoderSecuritySpec
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
    public void DecodeHeaders_should_reject_duplicate_method_pseudo_header()
    {
        var headers = new List<HpackHeader>
        {
            new(":method", "GET"),
            new(":method", "POST"),
            new(":path", "/"),
            new(":scheme", "https"),
            new(":authority", "example.com"),
        };

        var encoded = EncodeHeaders(headers);
        var state = BuildStreamState(encoded);

        var ex = Assert.Throws<HttpProtocolException>(() =>
            _decoder.DecodeHeadersToFeature(streamId: 1, endStream: true, state));

        Assert.Contains("Duplicate", ex.Message);
        Assert.Contains(":method", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void DecodeHeaders_should_reject_duplicate_path_pseudo_header()
    {
        var headers = new List<HpackHeader>
        {
            new(":method", "GET"),
            new(":path", "/index.html"),
            new(":path", "/other.html"),
            new(":scheme", "https"),
            new(":authority", "example.com"),
        };

        var encoded = EncodeHeaders(headers);
        var state = BuildStreamState(encoded);

        var ex = Assert.Throws<HttpProtocolException>(() =>
            _decoder.DecodeHeadersToFeature(streamId: 1, endStream: true, state));

        Assert.Contains("Duplicate", ex.Message);
        Assert.Contains(":path", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void DecodeHeaders_should_reject_pseudo_header_after_regular_header()
    {
        var headers = new List<HpackHeader>
        {
            new(":method", "GET"),
            new(":path", "/"),
            new("x-custom", "value"),
            new(":scheme", "https"),
            new(":authority", "example.com"),
        };

        var encoded = EncodeHeaders(headers);
        var state = BuildStreamState(encoded);

        var ex = Assert.Throws<HttpProtocolException>(() =>
            _decoder.DecodeHeadersToFeature(streamId: 1, endStream: true, state));

        Assert.Contains("appears after regular header", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void DecodeHeaders_should_reject_unknown_pseudo_header()
    {
        var headers = new List<HpackHeader>
        {
            new(":method", "GET"),
            new(":custom", "unknown-value"),
            new(":path", "/"),
            new(":scheme", "https"),
            new(":authority", "example.com"),
        };

        var encoded = EncodeHeaders(headers);
        var state = BuildStreamState(encoded);

        var ex = Assert.Throws<HttpProtocolException>(() =>
            _decoder.DecodeHeadersToFeature(streamId: 1, endStream: true, state));

        Assert.Contains("Unknown", ex.Message);
        Assert.Contains(":custom", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2.2")]
    public void DecodeHeaders_should_reject_connection_header()
    {
        var headers = new List<HpackHeader>
        {
            new(":method", "GET"),
            new(":path", "/"),
            new(":scheme", "https"),
            new(":authority", "example.com"),
            new("connection", "close"),
        };

        var encoded = EncodeHeaders(headers);
        var state = BuildStreamState(encoded);

        var ex = Assert.Throws<HttpProtocolException>(() =>
            _decoder.DecodeHeadersToFeature(streamId: 1, endStream: true, state));

        Assert.Contains("forbidden", ex.Message);
        Assert.Contains("Connection", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2.2")]
    public void DecodeHeaders_should_reject_transfer_encoding_header()
    {
        var headers = new List<HpackHeader>
        {
            new(":method", "POST"),
            new(":path", "/api/data"),
            new(":scheme", "https"),
            new(":authority", "example.com"),
            new("transfer-encoding", "chunked"),
        };

        var encoded = EncodeHeaders(headers);
        var state = BuildStreamState(encoded);

        var ex = Assert.Throws<HttpProtocolException>(() =>
            _decoder.DecodeHeadersToFeature(streamId: 1, endStream: true, state));

        Assert.Contains("forbidden", ex.Message);
        Assert.Contains("Transfer-Encoding", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2.2")]
    public void DecodeHeaders_should_reject_te_header_with_non_trailers_value()
    {
        var headers = new List<HpackHeader>
        {
            new(":method", "GET"),
            new(":path", "/"),
            new(":scheme", "https"),
            new(":authority", "example.com"),
            new("te", "gzip"),
        };

        var encoded = EncodeHeaders(headers);
        var state = BuildStreamState(encoded);

        var ex = Assert.Throws<HttpProtocolException>(() =>
            _decoder.DecodeHeadersToFeature(streamId: 1, endStream: true, state));

        Assert.Contains("TE header", ex.Message);
        Assert.Contains("trailers", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2.2")]
    public void DecodeHeaders_should_accept_te_header_with_trailers_value()
    {
        var headers = new List<HpackHeader>
        {
            new(":method", "GET"),
            new(":path", "/"),
            new(":scheme", "https"),
            new(":authority", "example.com"),
            new("te", "trailers"),
        };

        var encoded = EncodeHeaders(headers);
        var state = BuildStreamState(encoded);

        var feature = _decoder.DecodeHeadersToFeature(streamId: 1, endStream: true, state);

        Assert.NotNull(feature);
        Assert.Equal("GET", feature.Method);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.5")]
    public void DecodeHeaders_CONNECT_with_path_should_reject()
    {
        var headers = new List<HpackHeader>
        {
            new(":method", "CONNECT"),
            new(":path", "/tunnel"),
            new(":authority", "example.com:443"),
        };

        var encoded = EncodeHeaders(headers);
        var state = BuildStreamState(encoded);

        var ex = Assert.Throws<HttpProtocolException>(() =>
            _decoder.DecodeHeadersToFeature(streamId: 1, endStream: true, state));

        Assert.Contains("CONNECT", ex.Message);
        Assert.Contains(":path", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.5")]
    public void DecodeHeaders_CONNECT_with_scheme_should_reject()
    {
        var headers = new List<HpackHeader>
        {
            new(":method", "CONNECT"),
            new(":scheme", "https"),
            new(":authority", "example.com:443"),
        };

        var encoded = EncodeHeaders(headers);
        var state = BuildStreamState(encoded);

        var ex = Assert.Throws<HttpProtocolException>(() =>
            _decoder.DecodeHeadersToFeature(streamId: 1, endStream: true, state));

        Assert.Contains("CONNECT", ex.Message);
        Assert.Contains(":scheme", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.5")]
    public void DecodeHeaders_CONNECT_without_authority_should_reject()
    {
        var headers = new List<HpackHeader>
        {
            new(":method", "CONNECT"),
        };

        var encoded = EncodeHeaders(headers);
        var state = BuildStreamState(encoded);

        var ex = Assert.Throws<HttpProtocolException>(() =>
            _decoder.DecodeHeadersToFeature(streamId: 1, endStream: true, state));

        Assert.Contains("CONNECT", ex.Message);
        Assert.Contains(":authority", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-10.5.1")]
    public void DecodeHeaders_should_reject_single_header_exceeding_max_size()
    {
        var maxHeaderSize = 64;
        var decoder = new Http2ServerDecoder(DefaultDecoderOptions() with { MaxHeaderBytes = maxHeaderSize });

        var largeValue = new string('x', 100);
        var headers = new List<HpackHeader>
        {
            new(":method", "GET"),
            new(":path", "/"),
            new(":scheme", "https"),
            new(":authority", "example.com"),
            new("x-large", largeValue),
        };

        var encoded = EncodeHeaders(headers);
        var state = BuildStreamState(encoded);

        var ex = Assert.Throws<HttpProtocolException>(() =>
            decoder.DecodeHeadersToFeature(streamId: 1, endStream: true, state));

        Assert.Contains("exceeds MaxHeaderSize", ex.Message);
        Assert.Contains("64", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-10.5.1")]
    public void DecodeHeaders_should_reject_total_headers_exceeding_max_total_size()
    {
        var maxTotalHeaderSize = 128;
        var decoder = new Http2ServerDecoder(DefaultDecoderOptions() with { MaxFieldSectionSize = maxTotalHeaderSize });

        var headers = new List<HpackHeader>
        {
            new(":method", "GET"),
            new(":path", "/"),
            new(":scheme", "https"),
            new(":authority", "example.com"),
            new("x-header1", "aaaabbbbccccdddd"),
            new("x-header2", "eeeeffffgggghhhh"),
            new("x-header3", "iiiijjjjkkkkllll"),
            new("x-header4", "mmmmnnnnoooopppp"),
        };

        var encoded = EncodeHeaders(headers);
        var state = BuildStreamState(encoded);

        // Total header-list size is enforced at the HPACK layer (RFC 9113 §6.5.2 / MAX_HEADER_LIST_SIZE),
        // which rejects during decode before the full list is materialized — a COMPRESSION_ERROR.
        var ex = Assert.Throws<HpackException>(() =>
            decoder.DecodeHeadersToFeature(streamId: 1, endStream: true, state));

        Assert.Contains("MAX_HEADER_LIST_SIZE", ex.Message);
        Assert.Contains("128", ex.Message);
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