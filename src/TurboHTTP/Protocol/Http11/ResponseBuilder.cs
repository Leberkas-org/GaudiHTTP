using System.Buffers;
using System.Net;
using TurboHTTP.Internal;

namespace TurboHTTP.Protocol.Http11;

internal static class ResponseBuilder
{
    internal static bool IsNoBodyResponse(int statusCode)
        => statusCode is >= 100 and < 200 or 204 or 304;

    internal static bool IsContentHeader(string name) =>
        name.StartsWith("content-", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("content-length", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("content-type", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("allow", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("expires", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("last-modified", StringComparison.OrdinalIgnoreCase);

    internal static HttpResponseMessage BuildNoBody(
        int statusCode, string reasonPhrase,
        Dictionary<string, List<string>> headers)
    {
        var response = new HttpResponseMessage
        {
            StatusCode = (HttpStatusCode)statusCode,
            ReasonPhrase = reasonPhrase,
            Version = HttpVersion.Version11
        };

        var emptyContent = new ByteArrayContent([]);

        foreach (var (name, values) in headers)
        {
            foreach (var value in values)
            {
                if (IsContentHeader(name))
                {
                    emptyContent.Headers.TryAddWithoutValidation(name, value);
                }
                else
                {
                    response.Headers.TryAddWithoutValidation(name, value);
                }
            }
        }

        response.Content = emptyContent;
        return response;
    }

    internal static HttpResponseMessage Build(
        int statusCode, string reasonPhrase,
        Dictionary<string, List<string>> headers,
        IMemoryOwner<byte>? bodyOwner, int bodyLength,
        Dictionary<string, List<string>>? trailers = null)
    {
        var response = new HttpResponseMessage
        {
            StatusCode = (HttpStatusCode)statusCode,
            ReasonPhrase = reasonPhrase,
            Version = HttpVersion.Version11
        };

        HttpContent content = bodyOwner is not null
            ? new PooledBodyContent(bodyOwner, bodyLength)
            : new ByteArrayContent([]);

        foreach (var (name, values) in headers)
        {
            foreach (var value in values)
            {
                if (IsContentHeader(name))
                {
                    content.Headers.TryAddWithoutValidation(name, value);
                }
                else
                {
                    response.Headers.TryAddWithoutValidation(name, value);
                }
            }
        }

        if (trailers is not null)
        {
            foreach (var (name, values) in trailers)
            {
                foreach (var value in values)
                {
                    response.TrailingHeaders.TryAddWithoutValidation(name, value);
                }
            }
        }

        response.Content = content;
        return response;
    }

    internal static HttpResponseMessage BuildFromRemainder(
        int statusCode, string reasonPhrase,
        Dictionary<string, List<string>> headers,
        ReadOnlySpan<byte> bodySpan)
    {
        var response = new HttpResponseMessage
        {
            StatusCode = (HttpStatusCode)statusCode,
            ReasonPhrase = reasonPhrase,
            Version = HttpVersion.Version11
        };

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
            foreach (var value in values)
            {
                if (IsContentHeader(name))
                {
                    content.Headers.TryAddWithoutValidation(name, value);
                }
                else
                {
                    response.Headers.TryAddWithoutValidation(name, value);
                }
            }
        }

        response.Content = content;
        return response;
    }
}
