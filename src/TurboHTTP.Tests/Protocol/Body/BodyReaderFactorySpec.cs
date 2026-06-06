using TurboHTTP.Protocol.Body;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Tests.Protocol.Body;

public sealed class BodyReaderFactorySpec
{
    private static readonly BodyDecoderOptions DefaultOptions = new()
    {
        StreamingThreshold = 64 * 1024,
        MaxBufferedBodySize = 64 * 1024,
        MaxStreamedBodySize = 8 * 1024 * 1024,
        MaxChunkExtensionLength = 256
    };

    [Fact(Timeout = 5000)]
    public void Create_should_return_null_reader_for_no_body()
    {
        var classification = new BodyClassification(BodyFraming.None, null);
        var (reader, decoder) = BodyReaderFactory.Create(classification, DefaultOptions);

        Assert.Null(reader);
        Assert.Null(decoder);
    }

    [Fact(Timeout = 5000)]
    public void Create_should_return_buffered_reader_for_small_content_length()
    {
        var classification = new BodyClassification(BodyFraming.Length, 100);
        var (reader, decoder) = BodyReaderFactory.Create(classification, DefaultOptions);

        Assert.NotNull(reader);
        Assert.IsType<BufferedBodyReader>(reader);
        Assert.Null(decoder);
        reader.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void Create_should_return_queued_reader_with_content_length_decoder_for_large_body()
    {
        var classification = new BodyClassification(BodyFraming.Length, 128 * 1024);
        var (reader, decoder) = BodyReaderFactory.Create(classification, DefaultOptions);

        Assert.NotNull(reader);
        Assert.IsType<QueuedBodyReader>(reader);
        Assert.NotNull(decoder);
        Assert.IsType<ContentLengthFramingDecoder>(decoder);
        reader.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void Create_should_return_queued_reader_with_chunked_decoder()
    {
        var classification = new BodyClassification(BodyFraming.Chunked, null);
        var (reader, decoder) = BodyReaderFactory.Create(classification, DefaultOptions);

        Assert.NotNull(reader);
        Assert.IsType<QueuedBodyReader>(reader);
        Assert.NotNull(decoder);
        Assert.IsType<ChunkedFramingDecoder>(decoder);
        reader.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void Create_should_return_queued_reader_with_close_delimited_decoder()
    {
        var classification = new BodyClassification(BodyFraming.Close, null);
        var (reader, decoder) = BodyReaderFactory.Create(classification, DefaultOptions);

        Assert.NotNull(reader);
        Assert.IsType<QueuedBodyReader>(reader);
        Assert.NotNull(decoder);
        Assert.IsType<CloseDelimitedFramingDecoder>(decoder);
        reader.Dispose();
    }
}
