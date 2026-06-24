using GaudiHTTP.Protocol.Body;

namespace GaudiHTTP.Tests.Protocol.Body;

public sealed class ChunkedFramingDecoderSpec
{
    [Fact(Timeout = 5000)]
    public void Decode_should_parse_single_chunk_and_terminator()
    {
        var decoder = new ChunkedFramingDecoder();
        decoder.Reset(long.MaxValue, maxChunkExtensionLength: 256);

        var input = "5\r\nhello\r\n0\r\n\r\n"u8;
        var bodyBytes = new List<byte>();
        var pos = 0;

        while (!decoder.IsComplete && pos < input.Length)
        {
            var result = decoder.Decode(input[pos..], out var consumed);
            if (!result.Body.IsEmpty)
            {
                bodyBytes.AddRange(result.Body.ToArray());
            }

            pos += consumed;
        }

        Assert.True(decoder.IsComplete);
        Assert.Equal("hello"u8.ToArray(), bodyBytes.ToArray());
    }

    [Fact(Timeout = 5000)]
    public void Decode_should_parse_multiple_chunks()
    {
        var decoder = new ChunkedFramingDecoder();
        decoder.Reset(long.MaxValue, maxChunkExtensionLength: 256);

        var input = "3\r\nabc\r\n2\r\nde\r\n0\r\n\r\n"u8;
        var bodyBytes = new List<byte>();
        var pos = 0;

        while (!decoder.IsComplete && pos < input.Length)
        {
            var result = decoder.Decode(input[pos..], out var consumed);
            if (!result.Body.IsEmpty)
            {
                bodyBytes.AddRange(result.Body.ToArray());
            }

            pos += consumed;
        }

        Assert.True(decoder.IsComplete);
        Assert.Equal("abcde"u8.ToArray(), bodyBytes.ToArray());
    }

    [Fact(Timeout = 5000)]
    public void Decode_should_handle_partial_input_across_calls()
    {
        var decoder = new ChunkedFramingDecoder();
        decoder.Reset(long.MaxValue, maxChunkExtensionLength: 256);

        var bodyBytes = new List<byte>();

        var r1 = decoder.Decode("5\r\nhel"u8, out _);
        if (!r1.Body.IsEmpty) bodyBytes.AddRange(r1.Body.ToArray());

        var r2 = decoder.Decode("lo\r\n0\r\n\r\n"u8, out _);
        if (!r2.Body.IsEmpty) bodyBytes.AddRange(r2.Body.ToArray());

        Assert.True(decoder.IsComplete);
        Assert.Equal("hello"u8.ToArray(), bodyBytes.ToArray());
    }

    [Fact(Timeout = 5000)]
    public void Decode_should_collect_trailers()
    {
        var decoder = new ChunkedFramingDecoder();
        decoder.Reset(long.MaxValue, maxChunkExtensionLength: 256);

        var input = "0\r\nX-Checksum: abc123\r\n\r\n"u8;
        decoder.Decode(input, out _);

        Assert.True(decoder.IsComplete);
        Assert.Single(decoder.Trailers);
        Assert.Equal("X-Checksum", decoder.Trailers[0].Name);
        Assert.Equal("abc123", decoder.Trailers[0].Value);
    }

    [Fact(Timeout = 5000)]
    public void Decode_should_reject_invalid_chunk_size()
    {
        var decoder = new ChunkedFramingDecoder();
        decoder.Reset(long.MaxValue, maxChunkExtensionLength: 256);

        Assert.Throws<HttpProtocolException>(
            () => decoder.Decode("ZZZZ\r\n"u8, out _));
    }

    [Fact(Timeout = 5000)]
    public void Decode_should_handle_chunk_extensions()
    {
        var decoder = new ChunkedFramingDecoder();
        decoder.Reset(long.MaxValue, maxChunkExtensionLength: 256);

        var input = "3;ext=val\r\nabc\r\n0\r\n\r\n"u8;
        var bodyBytes = new List<byte>();
        var pos = 0;

        while (!decoder.IsComplete && pos < input.Length)
        {
            var result = decoder.Decode(input[pos..], out var consumed);
            if (!result.Body.IsEmpty)
            {
                bodyBytes.AddRange(result.Body.ToArray());
            }

            pos += consumed;
        }

        Assert.Equal("abc"u8.ToArray(), bodyBytes.ToArray());
    }

    // Mirrors the real streaming caller (Http11ServerDecoder.Feed / Http11ClientDecoder.Feed):
    // advance by rawConsumed, re-pass span[pos..], and break on NeedMore (rawConsumed == 0).
    private static void FeedSegment(ChunkedFramingDecoder decoder, ReadOnlySpan<byte> segment, List<byte> body)
    {
        var pos = 0;
        while (pos < segment.Length)
        {
            var result = decoder.Decode(segment[pos..], out var consumed);
            pos += consumed;
            if (!result.Body.IsEmpty)
            {
                body.AddRange(result.Body.ToArray());
            }

            if (result.EndOfBody)
            {
                return;
            }

            if (consumed == 0)
            {
                break;
            }
        }
    }

    [Fact(Timeout = 5000)]
    public void Decode_should_not_duplicate_partial_chunk_size_line_after_chunk_data()
    {
        var decoder = new ChunkedFramingDecoder();
        decoder.Reset(long.MaxValue, maxChunkExtensionLength: 256);
        var body = new List<byte>();

        // Segment ends after complete chunk data + a partial next chunk-size line ("3\r").
        FeedSegment(decoder, "5\r\nHELLO\r\n3\r"u8, body);
        FeedSegment(decoder, "\nFOO\r\n0\r\n\r\n"u8, body);

        Assert.True(decoder.IsComplete);
        Assert.Equal("HELLOFOO"u8.ToArray(), body.ToArray());
    }

    [Fact(Timeout = 5000)]
    public void Decode_should_not_duplicate_partial_chunk_data_crlf_after_chunk_data()
    {
        var decoder = new ChunkedFramingDecoder();
        decoder.Reset(long.MaxValue, maxChunkExtensionLength: 256);
        var body = new List<byte>();

        // Segment ends after complete chunk data + a partial trailing CRLF ("\r").
        FeedSegment(decoder, "5\r\nHELLO\r"u8, body);
        FeedSegment(decoder, "\n3\r\nFOO\r\n0\r\n\r\n"u8, body);

        Assert.True(decoder.IsComplete);
        Assert.Equal("HELLOFOO"u8.ToArray(), body.ToArray());
    }

    [Fact(Timeout = 5000)]
    public void Decode_should_not_duplicate_partial_trailer_line()
    {
        var decoder = new ChunkedFramingDecoder();
        decoder.Reset(long.MaxValue, maxChunkExtensionLength: 256);
        var body = new List<byte>();

        // Segment ends after the last chunk + a partial trailer line ("X-Tr").
        FeedSegment(decoder, "3\r\nabc\r\n0\r\nX-Tr"u8, body);
        FeedSegment(decoder, "ailer: v\r\n\r\n"u8, body);

        Assert.True(decoder.IsComplete);
        Assert.Equal("abc"u8.ToArray(), body.ToArray());
        Assert.Single(decoder.Trailers);
        Assert.Equal("X-Trailer", decoder.Trailers[0].Name);
        Assert.Equal("v", decoder.Trailers[0].Value);
    }

    [Fact(Timeout = 5000)]
    public void Reset_should_allow_reuse()
    {
        var decoder = new ChunkedFramingDecoder();
        decoder.Reset(long.MaxValue, maxChunkExtensionLength: 256);
        decoder.Decode("0\r\n\r\n"u8, out _);
        Assert.True(decoder.IsComplete);

        decoder.Reset(long.MaxValue, maxChunkExtensionLength: 256);
        Assert.False(decoder.IsComplete);
    }
}
