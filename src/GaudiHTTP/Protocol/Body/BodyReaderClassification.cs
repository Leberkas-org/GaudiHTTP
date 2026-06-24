using GaudiHTTP.Protocol.Semantics;

namespace GaudiHTTP.Protocol.Body;

internal readonly struct BodyReaderClassification
{
    public bool HasBody { get; init; }
    public bool IsBuffered { get; init; }
    public bool IsChunked { get; init; }
    public bool HasContentLength { get; init; }
    public long ContentLength { get; init; }

    public static BodyReaderClassification FromBodyClassification(
        BodyClassification classification,
        BodyDecoderOptions options)
    {
        if (classification.Framing == BodyFraming.None)
        {
            return new BodyReaderClassification { HasBody = false };
        }

        var maxBody = options.MaxStreamedBodySize ?? long.MaxValue;

        if (classification.Framing == BodyFraming.Length)
        {
            var n = classification.ContentLength ?? 0;
            if (n > maxBody)
            {
                throw new HttpProtocolException(
                    string.Concat("Declared body length ", n.ToString(), " exceeds the configured limit ", maxBody.ToString(), "."));
            }

            var isBuffered = n <= options.StreamingThreshold && n <= options.MaxBufferedBodySize;
            return new BodyReaderClassification
            {
                HasBody = true,
                IsBuffered = isBuffered,
                HasContentLength = true,
                ContentLength = n
            };
        }

        if (classification.Framing == BodyFraming.Chunked)
        {
            return new BodyReaderClassification
            {
                HasBody = true,
                IsChunked = true
            };
        }

        // BodyFraming.Close
        return new BodyReaderClassification
        {
            HasBody = true
        };
    }
}
