using System.Net;

namespace TurboHTTP.Protocol.LineBased.Body;

internal static class BodyEncoderFactory
{
    public static IBodyEncoder? Create(Stream? bodyStream, long? contentLength, Version httpVersion, BodyEncoderOptions? options = null)
    {
        if (bodyStream is null)
        {
            return null;
        }

        options ??= BodyEncoderOptions.Default;

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
