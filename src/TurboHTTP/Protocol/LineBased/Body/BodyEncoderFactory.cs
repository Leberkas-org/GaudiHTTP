using System.Net;

namespace TurboHTTP.Protocol.LineBased.Body;

internal static class BodyEncoderFactory
{
    public static IBodyEncoder? Create(Stream? bodyStream, long? contentLength, Version httpVersion, BodyEncoderOptions options)
    {
        if (bodyStream is null)
        {
            return null;
        }

        if (httpVersion == HttpVersion.Version10)
        {
            return new ContentLengthBufferedBodyEncoder();
        }

        if (contentLength is not null)
        {
            return new ContentLengthStreamedBodyEncoder(options.ChunkSize);
        }

        return new ChunkedBodyEncoder(options.ChunkSize);
    }
}
