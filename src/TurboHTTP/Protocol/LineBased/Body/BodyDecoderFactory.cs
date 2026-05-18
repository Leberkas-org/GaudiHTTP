using System.Buffers;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Protocol.LineBased.Body;

internal static class BodyDecoderFactory
{
    public static IBodyDecoder Create(
        BodyClassification classification,
        long streamingThreshold,
        MemoryPool<byte> pool,
        long maxBufferedBodySize = 4_194_304,
        long? maxStreamedBodySize = null,
        long maxBodySize = 10_485_760)
    {
        switch (classification.Framing)
        {
            case BodyFraming.None:
                return new ContentLengthBufferedDecoder(0, pool);

            case BodyFraming.Length:
                {
                    var n = classification.ContentLength ?? 0;
                    if (n <= streamingThreshold && n <= maxBufferedBodySize)
                    {
                        return new ContentLengthBufferedDecoder((int)n, pool);
                    }

                    var effectiveMax = maxStreamedBodySize ?? maxBodySize;
                    return new ContentLengthStreamedDecoder(n, effectiveMax);
                }

            case BodyFraming.Chunked:
                return new ChunkedBodyDecoder(maxStreamedBodySize ?? maxBodySize);

            case BodyFraming.Close:
                return new CloseDelimitedBodyDecoder(maxStreamedBodySize ?? maxBodySize);

            default:
                throw new ArgumentOutOfRangeException(nameof(classification));
        }
    }
}
