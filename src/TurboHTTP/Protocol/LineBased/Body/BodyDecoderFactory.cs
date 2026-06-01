using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Protocol.LineBased.Body;

internal static class BodyDecoderFactory
{
    public static IBodyDecoder Create(BodyClassification classification, BodyDecoderOptions options)
    {
        switch (classification.Framing)
        {
            case BodyFraming.None:
                return new ContentLengthBufferedDecoder(0);

            case BodyFraming.Length:
            {
                var n = classification.ContentLength ?? 0;
                if (n <= options.StreamingThreshold && n <= options.MaxBufferedBodySize)
                {
                    return new ContentLengthBufferedDecoder((int)n);
                }

                var effectiveMax = options.MaxStreamedBodySize ?? long.MaxValue;
                return new ContentLengthStreamedDecoder(n, effectiveMax);
            }

            case BodyFraming.Chunked:
                return new ChunkedBodyDecoder(options.MaxStreamedBodySize ?? long.MaxValue, options.MaxChunkExtensionLength);

            case BodyFraming.Close:
                return new CloseDelimitedBodyDecoder(options.MaxStreamedBodySize ?? long.MaxValue);

            default:
                throw new ArgumentOutOfRangeException(nameof(classification));
        }
    }
}
