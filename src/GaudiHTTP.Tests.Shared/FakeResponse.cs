using System.Collections.Frozen;
using System.Text;

namespace GaudiHTTP.Tests.Shared;

public static class FakeResponse
{
    private static readonly FrozenDictionary<int, string> ReasonPhrases = new Dictionary<int, string>
    {
        [200] = "OK",
        [201] = "Created",
        [204] = "No Content",
        [301] = "Moved Permanently",
        [302] = "Found",
        [304] = "Not Modified",
        [307] = "Temporary Redirect",
        [308] = "Permanent Redirect",
        [400] = "Bad Request",
        [401] = "Unauthorized",
        [403] = "Forbidden",
        [404] = "Not Found",
        [429] = "Too Many Requests",
        [500] = "Internal Server Error",
        [502] = "Bad Gateway",
        [503] = "Service Unavailable"
    }.ToFrozenDictionary();

    private static string GetReason(int status) => ReasonPhrases.GetValueOrDefault(status, "Unknown");

    public static byte[] Http10(int status, string? body = null, params (string Name, string Value)[] headers)
        => BuildHttp1("HTTP/1.0", status, body, headers);

    public static byte[] Http11(int status, string? body = null, params (string Name, string Value)[] headers)
        => BuildHttp1("HTTP/1.1", status, body, headers);

    private static byte[] BuildHttp1(string version, int status, string? body,
        (string Name, string Value)[] headers)
    {
        var sb = new StringBuilder();
        sb.Append(version).Append(' ').Append(status).Append(' ').Append(GetReason(status)).Append("\r\n");

        foreach (var (name, value) in headers)
        {
            sb.Append(name).Append(": ").Append(value).Append("\r\n");
        }

        var bodyBytes = body is not null ? Encoding.UTF8.GetBytes(body) : [];

        var hasContentLength = false;
        foreach (var (name, _) in headers)
        {
            if (string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                hasContentLength = true;
                break;
            }
        }

        if (!hasContentLength)
        {
            sb.Append("Content-Length: ").Append(bodyBytes.Length).Append("\r\n");
        }

        sb.Append("\r\n");

        var headerBytes = Encoding.Latin1.GetBytes(sb.ToString());
        var result = new byte[headerBytes.Length + bodyBytes.Length];
        headerBytes.CopyTo(result, 0);
        bodyBytes.CopyTo(result, headerBytes.Length);
        return result;
    }
}