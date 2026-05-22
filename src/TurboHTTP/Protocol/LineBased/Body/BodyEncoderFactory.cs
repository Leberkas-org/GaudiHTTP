using System.Net;
using System.Net.Http.Headers;

namespace TurboHTTP.Protocol.LineBased.Body;

internal static class BodyEncoderFactory
{
    public static IBodyEncoder? Create(
        HttpContent? content,
        Version httpVersion,
        HttpRequestHeaders? requestHeaders = null)
    {
        if (content is null)
        {
            return null;
        }

        if (httpVersion == HttpVersion.Version10)
        {
            return new ContentLengthBufferedBodyEncoder();
        }

        var contentLength = content.Headers.ContentLength;
        if (contentLength is null)
        {
            requestHeaders?.TransferEncodingChunked = true;

            return new ChunkedBodyEncoder();
        }

        return new ContentLengthStreamedBodyEncoder();
    }

    public static IBodyEncoder? Create(Stream? bodyStream, long? contentLength, Version httpVersion)
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
            return new ContentLengthStreamedBodyEncoder();
        }

        return new ChunkedBodyEncoder();
    }
}
