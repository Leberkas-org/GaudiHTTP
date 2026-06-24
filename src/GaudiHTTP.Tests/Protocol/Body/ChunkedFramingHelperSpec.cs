using System.Text;
using GaudiHTTP.Protocol.Body;

namespace GaudiHTTP.Tests.Protocol.Body;

public sealed class ChunkedFramingHelperSpec
{
    // GetFramedSize: hexDigits + CRLF(2) + data + CRLF(2)

    [Fact(Timeout = 5000)]
    public void GetFramedSize_should_be_correct_for_zero_bytes()
    {
        // "0" (1 hex digit) + \r\n + 0 data + \r\n = 1+2+0+2 = 5
        Assert.Equal(5, ChunkedFramingHelper.GetFramedSize(0));
    }

    [Fact(Timeout = 5000)]
    public void GetFramedSize_should_be_correct_for_one_byte()
    {
        // "1" (1 hex digit) + \r\n + 1 data + \r\n = 1+2+1+2 = 6
        Assert.Equal(6, ChunkedFramingHelper.GetFramedSize(1));
    }

    [Fact(Timeout = 5000)]
    public void GetFramedSize_should_be_correct_for_255_bytes()
    {
        // "ff" (2 hex digits) + \r\n + 255 data + \r\n = 2+2+255+2 = 261
        Assert.Equal(261, ChunkedFramingHelper.GetFramedSize(255));
    }

    [Fact(Timeout = 5000)]
    public void GetFramedSize_should_be_correct_for_256_bytes()
    {
        // "100" (3 hex digits) + \r\n + 256 data + \r\n = 3+2+256+2 = 263
        Assert.Equal(263, ChunkedFramingHelper.GetFramedSize(256));
    }

    [Fact(Timeout = 5000)]
    public void GetFramedSize_should_be_correct_for_16_bytes()
    {
        // "10" (2 hex digits) + \r\n + 16 data + \r\n = 2+2+16+2 = 22
        Assert.Equal(22, ChunkedFramingHelper.GetFramedSize(16));
    }

    [Fact(Timeout = 5000)]
    public void GetFramedSize_should_be_correct_for_65536_bytes()
    {
        // "10000" (5 hex digits) + \r\n + 65536 data + \r\n = 5+2+65536+2 = 65545
        Assert.Equal(65545, ChunkedFramingHelper.GetFramedSize(64 * 1024));
    }

    [Fact(Timeout = 5000)]
    public void WriteChunk_should_produce_valid_chunk_for_simple_data()
    {
        var data = "Hello"u8;
        var framedSize = ChunkedFramingHelper.GetFramedSize(data.Length);
        var destination = new byte[framedSize];

        var written = ChunkedFramingHelper.WriteChunk(data, destination);

        Assert.Equal(framedSize, written);
        var text = Encoding.ASCII.GetString(destination);
        Assert.Equal("5\r\nHello\r\n", text);
    }

    [Fact(Timeout = 5000)]
    public void WriteChunk_should_use_lowercase_hex()
    {
        // 255 bytes → hex "ff" (lowercase)
        var data = new byte[255];
        var framedSize = ChunkedFramingHelper.GetFramedSize(data.Length);
        var destination = new byte[framedSize];

        ChunkedFramingHelper.WriteChunk(data, destination);

        // First two bytes should be 'f', 'f'
        Assert.Equal((byte)'f', destination[0]);
        Assert.Equal((byte)'f', destination[1]);
        Assert.Equal((byte)'\r', destination[2]);
        Assert.Equal((byte)'\n', destination[3]);
    }

    [Fact(Timeout = 5000)]
    public void WriteChunk_should_return_correct_byte_count()
    {
        var data = new byte[100];
        var framedSize = ChunkedFramingHelper.GetFramedSize(data.Length);
        var destination = new byte[framedSize + 10];

        var written = ChunkedFramingHelper.WriteChunk(data, destination);

        Assert.Equal(framedSize, written);
    }

    [Fact(Timeout = 5000)]
    public void WriteChunk_should_frame_binary_data_correctly()
    {
        var data = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var framedSize = ChunkedFramingHelper.GetFramedSize(data.Length);
        var destination = new byte[framedSize];

        ChunkedFramingHelper.WriteChunk(data, destination);

        // Header: "4\r\n"
        Assert.Equal((byte)'4', destination[0]);
        Assert.Equal((byte)'\r', destination[1]);
        Assert.Equal((byte)'\n', destination[2]);
        // Data
        Assert.Equal(0xDE, destination[3]);
        Assert.Equal(0xAD, destination[4]);
        Assert.Equal(0xBE, destination[5]);
        Assert.Equal(0xEF, destination[6]);
        // Trailer: "\r\n"
        Assert.Equal((byte)'\r', destination[7]);
        Assert.Equal((byte)'\n', destination[8]);
    }

    [Fact(Timeout = 5000)]
    public void WriteTerminator_should_produce_0_CRLF_CRLF()
    {
        var destination = new byte[5];

        var written = ChunkedFramingHelper.WriteTerminator(destination);

        Assert.Equal(5, written);
        Assert.Equal("0\r\n\r\n"u8.ToArray(), destination);
    }

    [Fact(Timeout = 5000)]
    public void WriteTerminator_should_return_5()
    {
        var destination = new byte[10];

        var written = ChunkedFramingHelper.WriteTerminator(destination);

        Assert.Equal(5, written);
    }

    [Fact(Timeout = 5000)]
    public void GetFramedSize_WriteChunk_should_be_consistent()
    {
        // Verify that GetFramedSize predicts exactly how many bytes WriteChunk writes
        foreach (var size in new[] { 0, 1, 15, 16, 255, 256, 1000, 65535 })
        {
            var data = new byte[size];
            var framedSize = ChunkedFramingHelper.GetFramedSize(size);
            var destination = new byte[framedSize];

            var written = ChunkedFramingHelper.WriteChunk(data, destination);

            Assert.Equal(framedSize, written);
        }
    }

    [Fact(Timeout = 5000)]
    public void WriteChunk_followed_by_WriteTerminator_produces_valid_chunked_body()
    {
        var data = "test"u8;
        var chunkSize = ChunkedFramingHelper.GetFramedSize(data.Length);
        var totalSize = chunkSize + 5;
        var destination = new byte[totalSize];

        var offset = ChunkedFramingHelper.WriteChunk(data, destination.AsSpan());
        ChunkedFramingHelper.WriteTerminator(destination.AsSpan(offset));

        var text = Encoding.ASCII.GetString(destination);
        Assert.Equal("4\r\ntest\r\n0\r\n\r\n", text);
    }
}
