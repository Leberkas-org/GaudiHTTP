using System.Net;

namespace GaudiHTTP.Protocol.Semantics;

internal enum BodyFraming
{
    None,
    Length,
    Chunked,
    Close
}

internal readonly struct BodyClassification(BodyFraming framing, long? contentLength)
{
    public BodyFraming Framing { get; } = framing;
    public long? ContentLength { get; } = contentLength;
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

        if (!ContentLengthSemantics.BodyRequired((HttpStatusCode)statusCode, WellKnownHeaders.Get))
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

            // RFC 9112 §6.1: chunked MUST be the final transfer coding and applied only once.
            // A substring match ("chunked, gzip", "x-chunked-foo") would let a body of unknown length
            // be parsed as the next request (request smuggling), so tokenize and inspect the final coding.
            if (FinalCodingIsChunked(te))
            {
                return new BodyClassification(BodyFraming.Chunked, null);
            }

            // Transfer-Encoding present but the final coding is not chunked.
            // Request: length is unreliable — reject (400). Response: length is determined by
            // reading until the connection closes (RFC 9112 §6.1).
            if (!isResponse)
            {
                throw new HttpProtocolException(
                    "Transfer-Encoding present but the final coding is not 'chunked'; rejected as unreliable body length (RFC 9112 §6.1).");
            }

            return new BodyClassification(BodyFraming.Close, null);
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

    /// <summary>
    /// Returns true only when the comma-separated transfer-coding list ends with exactly "chunked"
    /// and "chunked" appears nowhere else (RFC 9112 §6.1: applied once, as the final coding).
    /// </summary>
    private static bool FinalCodingIsChunked(string transferEncoding)
    {
        var codings = transferEncoding.Split(',');
        var finalIndex = codings.Length - 1;

        if (!codings[finalIndex].Trim().Equals(WellKnownHeaders.ChunkedValue, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        for (var i = 0; i < finalIndex; i++)
        {
            if (codings[i].Trim().Equals(WellKnownHeaders.ChunkedValue, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
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