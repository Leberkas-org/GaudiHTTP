using System.Buffers;
using System.Text;
using TurboHTTP.Protocol.Body;

namespace TurboHTTP.Tests.Protocol.Body;

public sealed class ChunkedFramingEncoderSpec
{
    [Fact(Timeout = 5000)]
    public void Headroom_should_accommodate_max_hex_digits_plus_crlf()
    {
        var encoder = new ChunkedFramingEncoder(maxChunkSize: 4 * 1024);
        Assert.True(encoder.Headroom >= 3 + 2);
    }

    [Fact(Timeout = 5000)]
    public void Trailer_should_be_two_for_crlf()
    {
        var encoder = new ChunkedFramingEncoder(maxChunkSize: 4 * 1024);
        Assert.Equal(2, encoder.Trailer);
    }

    [Fact(Timeout = 5000)]
    public void Frame_should_produce_valid_chunked_encoding()
    {
        var encoder = new ChunkedFramingEncoder(maxChunkSize: 4 * 1024);
        var headroom = encoder.Headroom;
        var dataLen = 5;
        var totalSize = headroom + dataLen + encoder.Trailer;
        var owner = MemoryPool<byte>.Shared.Rent(totalSize);

        "hello"u8.CopyTo(owner.Memory.Span[headroom..]);

        var framed = encoder.Frame(owner, headroom, dataLen);
        var text = Encoding.ASCII.GetString(framed.Span);

        Assert.Contains("5\r\nhello\r\n", text);
        owner.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void GetTerminator_should_return_zero_chunk()
    {
        var encoder = new ChunkedFramingEncoder(maxChunkSize: 4 * 1024);
        var terminator = encoder.GetTerminator();
        var text = Encoding.ASCII.GetString(terminator.Memory.Span);

        Assert.Equal("0\r\n\r\n", text);
        terminator.Owner.Dispose();
    }
}
