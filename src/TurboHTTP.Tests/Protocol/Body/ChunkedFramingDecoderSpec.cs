using TurboHTTP.Protocol.Body;

namespace TurboHTTP.Tests.Protocol.Body;

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
