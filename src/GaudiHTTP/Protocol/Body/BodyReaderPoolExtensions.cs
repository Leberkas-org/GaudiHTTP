using GaudiHTTP.Pooling;

namespace GaudiHTTP.Protocol.Body;

internal static class BodyReaderPoolExtensions
{
    public static (IBodyReader? Reader, IFramingDecoder? Decoder) RentBodyReader(
        this ConnectionPoolContext pool,
        BodyReaderClassification classification,
        BodyDecoderOptions options)
    {
        if (!classification.HasBody)
        {
            return (null, null);
        }

        if (classification.IsBuffered)
        {
            var reader = pool.Rent(static () => new BufferedBodyReader());
            reader.Reset((int)classification.ContentLength);
            return (reader, null);
        }

        var streamingReader = pool.Rent(static () => new QueuedBodyReader(capacity: 8));

        if (classification.IsChunked)
        {
            var decoder = pool.Rent(static () => new ChunkedFramingDecoder());
            decoder.Reset(options.MaxStreamedBodySize ?? long.MaxValue, options.MaxChunkExtensionLength);
            return (streamingReader, decoder);
        }

        if (classification.HasContentLength)
        {
            var decoder = pool.Rent(static () => new ContentLengthFramingDecoder());
            decoder.Reset(classification.ContentLength);
            return (streamingReader, decoder);
        }

        var closeDecoder = pool.Rent(static () => new CloseDelimitedFramingDecoder());
        closeDecoder.Reset(options.MaxStreamedBodySize ?? long.MaxValue);
        return (streamingReader, closeDecoder);
    }

    public static void ReturnBodyReader(
        this ConnectionPoolContext pool,
        IBodyReader? reader,
        IFramingDecoder? decoder)
    {
        switch (reader)
        {
            case QueuedBodyReader qr:
                pool.Return(qr);
                break;
            case BufferedBodyReader br:
                pool.Return(br);
                break;
        }

        switch (decoder)
        {
            case ChunkedFramingDecoder cd:
                pool.Return(cd);
                break;
            case ContentLengthFramingDecoder cl:
                pool.Return(cl);
                break;
            case CloseDelimitedFramingDecoder cld:
                pool.Return(cld);
                break;
        }
    }
}
