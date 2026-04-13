using System.Net;

namespace TurboHTTP.Protocol.Http10;

internal static class ResponseBuilder
{
    private static readonly HashSet<string> ContentHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Content-Type", WellKnownHeaders.Names.ContentLength,
        WellKnownHeaders.Names.ContentEncoding, "Content-Language", "Content-Location", "Content-MD5",
        "Content-Range", "Content-Disposition", "Expires", "Last-Modified"
    };

    internal static HttpResponseMessage BuildHttp09(ReadOnlySpan<byte> body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Version = new Version(0, 9),
            Content = new ByteArrayContent(body.ToArray())
        };
    }

    internal static HttpResponseMessage Build(string statusLine, Dictionary<string, List<string>> headers,
        ReadOnlySpan<byte> body)
    {
        var parts = statusLine.Split(' ', 3);
        var statusCode = 500;
        if (parts.Length >= 2 && int.TryParse(parts[1], out var code))
        {
            statusCode = code;
        }

        var reasonPhrase = parts.Length > 2 ? parts[2] : string.Empty;
        var response = new HttpResponseMessage((HttpStatusCode)statusCode)
        {
            ReasonPhrase = reasonPhrase,
            Version = HttpVersion.Version10
        };

        var content = new ByteArrayContent(body.ToArray());
        response.Content = content;

        foreach (var (name, values) in headers)
        {
            foreach (var value in values)
            {
                if (ContentHeaders.Contains(name))
                {
                    content.Headers.TryAddWithoutValidation(name, value);
                }
                else
                {
                    response.Headers.TryAddWithoutValidation(name, value);
                }
            }
        }

        return response;
    }
}
