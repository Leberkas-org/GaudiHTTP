using GaudiHTTP.Pooling;
using GaudiHTTP.Protocol.Body;

namespace GaudiHTTP.Tests.Protocol.Body;

public sealed class BodyReaderPoolExtensionsSpec
{
    private static readonly BodyDecoderOptions DefaultOptions = new()
    {
        StreamingThreshold = 64 * 1024,
        MaxBufferedBodySize = 64 * 1024,
        MaxStreamedBodySize = 8 * 1024 * 1024,
        MaxChunkExtensionLength = 256
    };

    [Fact(Timeout = 5000)]
    public void RentBodyReader_should_return_null_when_no_body()
    {
        var pool = new ConnectionObjectPool();
        var (reader, decoder) = pool.RentBodyReader(
            new BodyReaderClassification { HasBody = false },
            DefaultOptions);

        Assert.Null(reader);
        Assert.Null(decoder);
    }

    [Fact(Timeout = 5000)]
    public void RentBodyReader_should_return_buffered_reader_for_buffered_classification()
    {
        var pool = new ConnectionObjectPool();
        var (reader, decoder) = pool.RentBodyReader(
            new BodyReaderClassification { HasBody = true, IsBuffered = true, ContentLength = 100 },
            DefaultOptions);

        Assert.IsType<BufferedBodyReader>(reader);
        Assert.Null(decoder);
        pool.ReturnBodyReader(reader, decoder);
    }

    [Fact(Timeout = 5000)]
    public void RentBodyReader_should_return_queued_reader_with_chunked_decoder()
    {
        var pool = new ConnectionObjectPool();
        var (reader, decoder) = pool.RentBodyReader(
            new BodyReaderClassification { HasBody = true, IsChunked = true },
            DefaultOptions);

        Assert.IsType<QueuedBodyReader>(reader);
        Assert.IsType<ChunkedFramingDecoder>(decoder);
        pool.ReturnBodyReader(reader, decoder);
    }

    [Fact(Timeout = 5000)]
    public void RentBodyReader_should_return_queued_reader_with_content_length_decoder()
    {
        var pool = new ConnectionObjectPool();
        var (reader, decoder) = pool.RentBodyReader(
            new BodyReaderClassification { HasBody = true, HasContentLength = true, ContentLength = 1024 },
            DefaultOptions);

        Assert.IsType<QueuedBodyReader>(reader);
        Assert.IsType<ContentLengthFramingDecoder>(decoder);
        pool.ReturnBodyReader(reader, decoder);
    }

    [Fact(Timeout = 5000)]
    public void RentBodyReader_should_return_queued_reader_with_close_delimited_decoder_when_no_framing_hint()
    {
        var pool = new ConnectionObjectPool();
        var (reader, decoder) = pool.RentBodyReader(
            new BodyReaderClassification { HasBody = true },
            DefaultOptions);

        Assert.IsType<QueuedBodyReader>(reader);
        Assert.IsType<CloseDelimitedFramingDecoder>(decoder);
        pool.ReturnBodyReader(reader, decoder);
    }

    [Fact(Timeout = 5000)]
    public void RentBodyReader_should_reuse_pooled_instances_after_return()
    {
        var pool = new ConnectionObjectPool();
        var (reader1, decoder1) = pool.RentBodyReader(
            new BodyReaderClassification { HasBody = true, HasContentLength = true, ContentLength = 1024 },
            DefaultOptions);

        pool.ReturnBodyReader(reader1, decoder1);

        var (reader2, decoder2) = pool.RentBodyReader(
            new BodyReaderClassification { HasBody = true, HasContentLength = true, ContentLength = 2048 },
            DefaultOptions);

        Assert.Same(reader1, reader2);
        Assert.Same(decoder1, decoder2);
        pool.ReturnBodyReader(reader2, decoder2);
    }
}
