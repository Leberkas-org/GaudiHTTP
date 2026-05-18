using System.Net;

namespace TurboHTTP.Protocol.Semantics;

internal enum BodyFraming
{
    None,
    Length,
    Chunked,
    Close,
}

internal readonly struct BodyClassification
{
    public BodyFraming Framing { get; }
    public long? ContentLength { get; }

    public BodyClassification(BodyFraming framing, long? contentLength)
    {
        Framing = framing;
        ContentLength = contentLength;
    }
}

internal static class BodySemantics
{
    public static BodyClassification ClassifyResponse(
        int statusCode,
        HeaderCollection headers,
        Version version,
        bool requestMethodWasHead,
        bool connectionWillClose)
    {
        if (requestMethodWasHead)
        {
            return new BodyClassification(BodyFraming.None, null);
        }

        if (!ContentLengthSemantics.BodyRequired((HttpStatusCode)statusCode, "GET"))
        {
            return new BodyClassification(BodyFraming.None, null);
        }

        return ClassifyFraming(headers, version, isResponse: true);
    }

    public static BodyClassification ClassifyRequest(
        HttpMethod method,
        HeaderCollection headers,
        Version version)
    {
        return ClassifyFraming(headers, version, isResponse: false);
    }

    private static BodyClassification ClassifyFraming(
        HeaderCollection headers,
        Version version,
        bool isResponse)
    {
        var te = headers.GetCombined(WellKnownHeaders.TransferEncoding);
        var cl = headers.GetCombined(WellKnownHeaders.ContentLength);

        if (te is not null && cl is not null)
        {
            throw new HttpProtocolException(
                "Both Transfer-Encoding and Content-Length are present; rejected as potential request smuggling.");
        }

        if (te is not null)
        {
            if (version.Equals(HttpVersion.Version10))
            {
                throw new HttpProtocolException("Transfer-Encoding not allowed in HTTP/1.0 messages.");
            }

            if (te.Contains(WellKnownHeaders.ChunkedValue, StringComparison.OrdinalIgnoreCase))
            {
                return new BodyClassification(BodyFraming.Chunked, null);
            }
        }

        if (cl is not null)
        {
            var clValue = NormalizeContentLength(cl);
            if (!ContentLengthSemantics.TryParse(clValue, out var n))
            {
                throw new HttpProtocolException(string.Concat("Invalid Content-Length value '", cl, "'."));
            }

            return new BodyClassification(BodyFraming.Length, n);
        }

        if (isResponse)
        {
            return new BodyClassification(BodyFraming.Close, null);
        }

        return new BodyClassification(BodyFraming.None, null);
    }

    private static string NormalizeContentLength(string combined)
    {
        if (!combined.Contains(','))
        {
            return combined;
        }

        var parts = combined.Split(',');
        var first = parts[0].Trim();
        foreach (var part in parts)
        {
            if (part.Trim() != first)
            {
                return combined;
            }
        }

        return first;
    }
}