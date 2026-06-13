using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Protocol.Body;

internal static class BodyReaderFactory
{
    public static (IBodyReader? Reader, IFramingDecoder? Decoder) Create(BodyClassification classification, BodyDecoderOptions options)
    {
        switch (classification.Framing)
        {
            case BodyFraming.None:
                return (null, null);

            case BodyFraming.Length:
            {
                var n = classification.ContentLength ?? 0;

                // A Content-Length body announces its full size up front, so a value over the
                // configured limit is rejected immediately — without reading a single byte. The
                // chunked/close paths below pass MaxStreamedBodySize into their decoders; the
                // fixed-length path previously decoded `n` bytes with no limit, letting a client
                // declare an arbitrarily large body and bypass MaxRequestBodySize entirely.
                var maxBody = options.MaxStreamedBodySize ?? long.MaxValue;
                if (n > maxBody)
                {
                    throw new HttpProtocolException(
                        $"Declared body length {n} exceeds the configured limit {maxBody}.");
                }

                if (n <= options.StreamingThreshold && n <= options.MaxBufferedBodySize)
                {
                    var reader = new BufferedBodyReader();
                    reader.Reset((int)n);
                    return (reader, null);
                }

                var queued = new QueuedBodyReader(capacity: 4);
                queued.Reset();
                var decoder = new ContentLengthFramingDecoder();
                decoder.Reset(n);
                return (queued, decoder);
            }

            case BodyFraming.Chunked:
            {
                var queued = new QueuedBodyReader(capacity: 4);
                queued.Reset();
                var decoder = new ChunkedFramingDecoder();
                decoder.Reset(options.MaxStreamedBodySize ?? long.MaxValue, options.MaxChunkExtensionLength);
                return (queued, decoder);
            }

            case BodyFraming.Close:
            {
                var queued = new QueuedBodyReader(capacity: 4);
                queued.Reset();
                var decoder = new CloseDelimitedFramingDecoder();
                decoder.Reset(options.MaxStreamedBodySize ?? long.MaxValue);
                return (queued, decoder);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(classification));
        }
    }
}
