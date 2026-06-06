using System.Buffers;
using TurboHTTP.Protocol.Body;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Tests.Protocol.Body;

public sealed class ConnectionBodyPoolSpec
{
    private static readonly BodyDecoderOptions DecoderOptions = new()
    {
        StreamingThreshold = 64 * 1024,
        MaxBufferedBodySize = 64 * 1024,
        MaxStreamedBodySize = 8 * 1024 * 1024,
        MaxChunkExtensionLength = 256
    };

    [Fact(Timeout = 5000)]
    public void RentReader_should_return_buffered_reader_for_small_body()
    {
        using var pool = new ConnectionBodyPool();
        var classification = new BodyClassification(BodyFraming.Length, 100);

        var (reader, decoder) = pool.RentReader(classification, DecoderOptions);

        Assert.NotNull(reader);
        Assert.True(reader.IsBuffered);
        Assert.Null(decoder);
    }

    [Fact(Timeout = 5000)]
    public void RentReader_should_return_bridged_reader_for_large_body()
    {
        using var pool = new ConnectionBodyPool();
        var classification = new BodyClassification(BodyFraming.Length, 128 * 1024);

        var (reader, decoder) = pool.RentReader(classification, DecoderOptions);

        Assert.NotNull(reader);
        Assert.False(reader.IsBuffered);
        Assert.NotNull(decoder);
    }

    [Fact(Timeout = 5000)]
    public void RentReader_should_return_same_instance_on_reuse()
    {
        using var pool = new ConnectionBodyPool();
        var classification = new BodyClassification(BodyFraming.Length, 128 * 1024);

        var (reader1, _) = pool.RentReader(classification, DecoderOptions);
        pool.ReturnReader();
        var (reader2, _) = pool.RentReader(classification, DecoderOptions);

        Assert.Same(reader1, reader2);
    }

    [Fact(Timeout = 5000)]
    public void RentReader_should_return_null_for_no_body()
    {
        using var pool = new ConnectionBodyPool();
        var classification = new BodyClassification(BodyFraming.None, null);

        var (reader, decoder) = pool.RentReader(classification, DecoderOptions);

        Assert.Null(reader);
        Assert.Null(decoder);
    }

    private static readonly BodyEncoderOptions EncoderOptions = new() { ChunkSize = 16 * 1024 };

    private static ValueTask NoOpSend(IMemoryOwner<byte> owner, ReadOnlyMemory<byte> data)
    {
        owner.Dispose();
        return default;
    }

    [Fact(Timeout = 5000)]
    public void RentWriter_should_return_streaming_for_http10_with_known_length()
    {
        using var pool = new ConnectionBodyPool();

        var (writer, encoder) = pool.RentWriter(
            hasBody: true, contentLength: 256, System.Net.HttpVersion.Version10,
            EncoderOptions, NoOpSend);

        Assert.NotNull(writer);
        Assert.IsType<StreamingBodyWriter>(writer);
        Assert.NotNull(encoder);
        Assert.IsType<PassthroughFramingEncoder>(encoder);
    }

    [Fact(Timeout = 5000)]
    public void RentWriter_should_return_buffered_for_http10_with_unknown_length()
    {
        using var pool = new ConnectionBodyPool();

        var (writer, encoder) = pool.RentWriter(
            hasBody: true, contentLength: null, System.Net.HttpVersion.Version10,
            EncoderOptions, NoOpSend, onBufferedComplete: (_, _) => { });

        Assert.NotNull(writer);
        Assert.IsType<BufferedBodyWriter>(writer);
        Assert.Null(encoder);
    }

    [Fact(Timeout = 5000)]
    public void RentWriter_should_return_null_for_no_body()
    {
        using var pool = new ConnectionBodyPool();

        var (writer, encoder) = pool.RentWriter(
            hasBody: false, contentLength: null, System.Net.HttpVersion.Version11,
            EncoderOptions, NoOpSend);

        Assert.Null(writer);
        Assert.Null(encoder);
    }
}
