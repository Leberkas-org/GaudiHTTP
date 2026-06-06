using System.Net;

namespace TurboHTTP.Protocol.Body;

internal static class BodyWriterFactory
{
    public static (IBodyWriter? Writer, IFramingEncoder? Encoder) Create(
        bool hasBody, long? contentLength, Version httpVersion, BodyEncoderOptions options)
    {
        if (!hasBody)
        {
            return (null, null);
        }

        if (contentLength is not null)
        {
            return (new StreamingBodyWriter(), new PassthroughFramingEncoder());
        }

        if (httpVersion == HttpVersion.Version10)
        {
            return (new BufferedBodyWriter(), null);
        }

        return (new StreamingBodyWriter(), new ChunkedFramingEncoder(options.ChunkSize));
    }
}