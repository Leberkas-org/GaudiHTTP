using System.Text;
using TurboHTTP.Protocol.LineBased.Body;

namespace TurboHTTP.Tests.Protocol.LineBased.Body;

public sealed class ChunkedBodyDecoderSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7.1")]
    public async Task Decoder_should_decode_two_chunks_and_terminator()
    {
        var decoder = new ChunkedBodyDecoder();
        var data = "5\r\nhello\r\n6\r\n world\r\n0\r\n\r\n"u8.ToArray();
        Assert.True(decoder.Feed(data, out _));

        var content = Assert.IsType<StreamContent>(decoder.GetContent());
        var bytes = await content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal("hello world", Encoding.ASCII.GetString(bytes));
        decoder.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7.1.1")]
    public async Task Decoder_should_ignore_chunk_extensions()
    {
        var decoder = new ChunkedBodyDecoder();
        var data = "5;ext=foo\r\nhello\r\n0\r\n\r\n"u8.ToArray();
        Assert.True(decoder.Feed(data, out _));

        var content = Assert.IsType<StreamContent>(decoder.GetContent());
        var bytes = await content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal("hello", Encoding.ASCII.GetString(bytes));
        decoder.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7.1")]
    public void Decoder_should_signal_NeedMore_when_chunk_incomplete()
    {
        var decoder = new ChunkedBodyDecoder();
        var data = "5\r\nhel"u8.ToArray();
        Assert.False(decoder.Feed(data, out _));
        decoder.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void Decoder_should_reject_invalid_chunk_size()
    {
        var decoder = new ChunkedBodyDecoder();
        var data = "XYZ\r\n"u8.ToArray();
        Assert.Throws<HttpProtocolException>(() => decoder.Feed(data, out _));
        decoder.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.5")]
    public async Task Decoder_should_accept_allowed_trailer_fields()
    {
        var decoder = new ChunkedBodyDecoder();
        var data = "5\r\nhello\r\n0\r\nX-Custom-Trailer: value\r\n\r\n"u8.ToArray();
        Assert.True(decoder.Feed(data, out _));

        var content = Assert.IsType<StreamContent>(decoder.GetContent());
        var bytes = await content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal("hello", Encoding.ASCII.GetString(bytes));
        decoder.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.5")]
    public async Task Decoder_should_skip_prohibited_trailer_fields()
    {
        var decoder = new ChunkedBodyDecoder();
        var data = "5\r\nhello\r\n0\r\nTransfer-Encoding: chunked\r\nX-Custom: ok\r\n\r\n"u8.ToArray();
        Assert.True(decoder.Feed(data, out _));

        var content = Assert.IsType<StreamContent>(decoder.GetContent());
        var bytes = await content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal("hello", Encoding.ASCII.GetString(bytes));
        decoder.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.5")]
    public void Decoder_should_collect_allowed_trailer_fields()
    {
        var decoder = new ChunkedBodyDecoder();
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
        var decoder = new ChunkedBodyDecoder();
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
        var decoder = new ChunkedBodyDecoder();
        var data = "5\r\nhello\r\n0\r\n\r\n"u8.ToArray();
        Assert.True(decoder.Feed(data, out _));

        Assert.Empty(decoder.Trailers);
        decoder.Dispose();
    }
}