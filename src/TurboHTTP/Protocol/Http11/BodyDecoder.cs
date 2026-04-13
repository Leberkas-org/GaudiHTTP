using System.Buffers;
using System.Net;
using TurboHTTP.Internal;

namespace TurboHTTP.Protocol.Http11;

internal static class BodyDecoder
{
    internal static bool IsNoBodyResponse(int statusCode) =>
        statusCode is >= 100 and < 200 or 204 or 304;

    internal static bool IsContentHeader(string name) =>
        name.StartsWith("content-", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("content-length", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("content-type", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("allow", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("expires", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("last-modified", StringComparison.OrdinalIgnoreCase);

    internal static int? GetContentLengthHeader(Dictionary<string, List<string>> headers)
    {
        if (!headers.TryGetValue(WellKnownHeaders.Names.ContentLength, out var values) || values.Count == 0)
        {
            return null;
        }

        // RFC 9112 Section 6.3: Multiple Content-Length with different values is error
        if (values.Count > 1)
        {
            var first = values[0];
            for (var i = 1; i < values.Count; i++)
            {
                if (!values[i].Equals(first, StringComparison.Ordinal))
                {
                    throw new HttpDecoderException(
                        HttpDecoderError.MultipleContentLengthValues,
                        $"Values '{first}' and '{values[i]}' conflict.");
                }
            }
        }

        return int.TryParse(values[0], out var len) && len >= 0 ? len : null;
    }

    internal static string? GetSingleHeader(Dictionary<string, List<string>> headers, string name) =>
        headers.TryGetValue(name, out var values) && values.Count > 0
            ? values[0]
            : null;

    internal static HttpResponseMessage BuildResponseFromRemainder(
        int statusCode, string reasonPhrase,
        Dictionary<string, List<string>> headers, ReadOnlySpan<byte> bodySpan)
    {
        var response = new HttpResponseMessage
        {
            StatusCode = (HttpStatusCode)statusCode,
            ReasonPhrase = reasonPhrase,
            Version = HttpVersion.Version11
        };

        foreach (var (name, values) in headers)
        {
            foreach (var value in values)
            {
                response.Headers.TryAddWithoutValidation(name, value);
            }
        }

        HttpContent content;
        if (!bodySpan.IsEmpty)
        {
            var owner = MemoryPool<byte>.Shared.Rent(bodySpan.Length);
            bodySpan.CopyTo(owner.Memory.Span);
            content = new PooledBodyContent(owner, bodySpan.Length);
        }
        else
        {
            content = new ByteArrayContent([]);
        }

        foreach (var (name, values) in headers)
        {
            if (!IsContentHeader(name))
            {
                continue;
            }

            foreach (var value in values)
            {
                content.Headers.TryAddWithoutValidation(name, value);
            }
        }

        response.Content = content;
        return response;
    }
}
