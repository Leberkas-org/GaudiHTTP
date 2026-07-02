using GaudiHTTP.Pooling;

namespace GaudiHTTP.Protocol.Body;

internal static class BodyReaderPoolExtensions
{
    public static (IBodyReader? Reader, IFramingDecoder? Decoder) RentBodyReader(
        BodyReaderClassification classification,
        BodyDecoderOptions options)
    {
        if (!classification.HasBody)
        {
            return (null, null);
        }

        var pool = ConnectionObjectPool.Instance;

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
            decoder.Reset(options.MaxStreamedBodySize ?? long.MaxValue, options.MaxChunkExtensionLength,
                options.MaxChunkedControlLineLength, options.MaxChunkedTrailerSize);
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

    public static void ReturnBodyReader(IBodyReader? reader, IFramingDecoder? decoder)
    {
        (reader as IDisposable)?.Dispose();
        (decoder as IDisposable)?.Dispose();
    }
}
