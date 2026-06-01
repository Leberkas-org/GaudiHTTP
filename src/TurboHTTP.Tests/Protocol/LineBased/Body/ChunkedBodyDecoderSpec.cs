using System.Text;
using TurboHTTP.Protocol.LineBased.Body;

namespace TurboHTTP.Tests.Protocol.LineBased.Body;

public sealed class ChunkedBodyDecoderSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7.1")]
    public async Task Decoder_should_decode_two_chunks_and_terminator()
    {
        var decoder = new ChunkedBodyDecoder(maxBodySize: 10 * 1024 * 1024, maxChunkExtensionLength: int.MaxValue);
        var data = "5\r\nhello\r\n6\r\n world\r\n0\r\n\r\n"u8.ToArray();
        Assert.True(decoder.Feed(data, out _));

        var bodyStream = decoder.GetBodyStream();
        var content = new StreamContent(bodyStream);
        var bytes = await content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal("hello world", Encoding.ASCII.GetString(bytes));
        decoder.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7.1.1")]
    public async Task Decoder_should_ignore_chunk_extensions()
    {
        var decoder = new ChunkedBodyDecoder(maxBodySize: 10 * 1024 * 1024, maxChunkExtensionLength: int.MaxValue);
        var data = "5;ext=foo\r\nhello\r\n0\r\n\r\n"u8.ToArray();
        Assert.True(decoder.Feed(data, out _));

        var bodyStream = decoder.GetBodyStream();
        var content = new StreamContent(bodyStream);
        var bytes = await content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal("hello", Encoding.ASCII.GetString(bytes));
        decoder.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7.1")]
    public void Decoder_should_signal_NeedMore_when_chunk_incomplete()
    {
        var decoder = new ChunkedBodyDecoder(maxBodySize: 10 * 1024 * 1024, maxChunkExtensionLength: int.MaxValue);
        var data = "5\r\nhel"u8.ToArray();
        Assert.False(decoder.Feed(data, out _));
        decoder.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void Decoder_should_reject_invalid_chunk_size()
    {
        var decoder = new ChunkedBodyDecoder(maxBodySize: 10 * 1024 * 1024, maxChunkExtensionLength: int.MaxValue);
        var data = "XYZ\r\n"u8.ToArray();
        Assert.Throws<HttpProtocolException>(() => decoder.Feed(data, out _));
        decoder.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7.1")]
    public void Decoder_should_reject_chunk_size_exceeding_int_max()
    {
        // "80000000" hex = 2^31, which overflows a signed Int32 to a negative chunk size, causing the
        // decoder to silently stall (Math.Min(negative, avail) takes nothing and never completes).
        var decoder = new ChunkedBodyDecoder(maxBodySize: 10L * 1024 * 1024 * 1024, maxChunkExtensionLength: int.MaxValue);
        var data = "80000000\r\n"u8.ToArray();
        Assert.Throws<HttpProtocolException>(() => decoder.Feed(data, out _));
        decoder.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7.1.2")]
    public void Decoder_should_reject_oversized_trailer_section()
    {
        var decoder = new ChunkedBodyDecoder(maxBodySize: 10 * 1024 * 1024, maxChunkExtensionLength: int.MaxValue);
        var sb = new StringBuilder("0\r\n");
        var line = "X-Trailer: " + new string('a', 200) + "\r\n";
        while (sb.Length < 128 * 1024)
        {
            sb.Append(line);
        }

        var data = Encoding.ASCII.GetBytes(sb.ToString());
        Assert.Throws<HttpProtocolException>(() => decoder.Feed(data, out _));
        decoder.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7.1")]
    public void Decoder_should_reject_overlong_chunk_size_line()
    {
        var decoder = new ChunkedBodyDecoder(maxBodySize: 10 * 1024 * 1024, maxChunkExtensionLength: 256);
        // A chunk-size line that never terminates would otherwise grow the stash buffer without bound.
        var data = Encoding.ASCII.GetBytes(new string('a', 128 * 1024));
        Assert.Throws<HttpProtocolException>(() => decoder.Feed(data, out _));
        decoder.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.5")]
    public async Task Decoder_should_accept_allowed_trailer_fields()
    {
        var decoder = new ChunkedBodyDecoder(maxBodySize: 10 * 1024 * 1024, maxChunkExtensionLength: int.MaxValue);
        var data = "5\r\nhello\r\n0\r\nX-Custom-Trailer: value\r\n\r\n"u8.ToArray();
        Assert.True(decoder.Feed(data, out _));

        var bodyStream = decoder.GetBodyStream();
        var content = new StreamContent(bodyStream);
        var bytes = await content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal("hello", Encoding.ASCII.GetString(bytes));
        decoder.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.5")]
    public async Task Decoder_should_skip_prohibited_trailer_fields()
    {
        var decoder = new ChunkedBodyDecoder(maxBodySize: 10 * 1024 * 1024, maxChunkExtensionLength: int.MaxValue);
        var data = "5\r\nhello\r\n0\r\nTransfer-Encoding: chunked\r\nX-Custom: ok\r\n\r\n"u8.ToArray();
        Assert.True(decoder.Feed(data, out _));

        var bodyStream = decoder.GetBodyStream();
        var content = new StreamContent(bodyStream);
        var bytes = await content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal("hello", Encoding.ASCII.GetString(bytes));
        decoder.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.5")]
    public void Decoder_should_collect_allowed_trailer_fields()
    {
        var decoder = new ChunkedBodyDecoder(maxBodySize: 10 * 1024 * 1024, maxChunkExtensionLength: int.MaxValue);
        var data = "5\r\nhello\r\n0\r\nX-Checksum: abc123\r\nServer-Timing: dur=42\r\n\r\n"u8.ToArray();
        Assert.True(decoder.Feed(data, out _));

        Assert.Equal(2, decoder.Trailers.Count);
        Assert.Equal("abc123", decoder.Trailers[0].Value);
        Assert.Equal("dur=42", decoder.Trailers[1].Value);
        decoder.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.5")]
    public void Decoder_should_filter_prohibited_trailer_fields()
    {
        var decoder = new ChunkedBodyDecoder(maxBodySize: 10 * 1024 * 1024, maxChunkExtensionLength: int.MaxValue);
        var data = "5\r\nhello\r\n0\r\nX-Custom: ok\r\nTransfer-Encoding: chunked\r\nContent-Length: 5\r\n\r\n"u8.ToArray();
        Assert.True(decoder.Feed(data, out _));

        Assert.Single(decoder.Trailers);
        Assert.Equal("ok", decoder.Trailers[0].Value);
        decoder.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.5")]
    public void Decoder_should_have_empty_trailers_when_none_present()
    {
        var decoder = new ChunkedBodyDecoder(maxBodySize: 10 * 1024 * 1024, maxChunkExtensionLength: int.MaxValue);
        var data = "5\r\nhello\r\n0\r\n\r\n"u8.ToArray();
        Assert.True(decoder.Feed(data, out _));

        Assert.Empty(decoder.Trailers);
        decoder.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7.1.1")]
    public void Decoder_should_reject_chunk_extension_exceeding_max_length()
    {
        var decoder = new ChunkedBodyDecoder(maxBodySize: 10 * 1024 * 1024, maxChunkExtensionLength: 8);
        var longExt = new string('a', 64);
        var data = Encoding.ASCII.GetBytes($"5;{longExt}=v\r\nhello\r\n0\r\n\r\n");

        Assert.Throws<HttpProtocolException>(() => decoder.Feed(data, out _));
        decoder.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7.1.1")]
    public void Decoder_should_accept_chunk_extension_within_max_length()
    {
        var decoder = new ChunkedBodyDecoder(maxBodySize: 10 * 1024 * 1024, maxChunkExtensionLength: 64);
        var data = "5;ext=foo\r\nhello\r\n0\r\n\r\n"u8.ToArray();

        Assert.True(decoder.Feed(data, out _));
        decoder.Dispose();
    }
}