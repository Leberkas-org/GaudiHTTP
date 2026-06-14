using System.Text;

namespace TurboHTTP.Protocol.Semantics;

internal static class ReasonPhrases
{
    private static readonly Dictionary<int, string> Table = new()
    {
        [100] = "Continue",
        [101] = "Switching Protocols",
        [200] = "OK",
        [201] = "Created",
        [202] = "Accepted",
        [204] = "No Content",
        [206] = "Partial Content",
        [301] = "Moved Permanently",
        [302] = "Found",
        [303] = "See Other",
        [304] = "Not Modified",
        [307] = "Temporary Redirect",
        [308] = "Permanent Redirect",
        [400] = "Bad Request",
        [401] = "Unauthorized",
        [403] = "Forbidden",
        [404] = "Not Found",
        [405] = "Method Not Allowed",
        [408] = "Request Timeout",
        [409] = "Conflict",
        [411] = "Length Required",
        [413] = "Content Too Large",
        [414] = "URI Too Long",
        [415] = "Unsupported Media Type",
        [416] = "Range Not Satisfiable",
        [417] = "Expectation Failed",
        [426] = "Upgrade Required",
        [500] = "Internal Server Error",
        [501] = "Not Implemented",
        [502] = "Bad Gateway",
        [503] = "Service Unavailable",
        [504] = "Gateway Timeout",
        [505] = "HTTP Version Not Supported"
    };

    public static string For(int code) => Table.GetValueOrDefault(code, "");

    /// <summary>
    /// Returns the canonical cached phrase for <paramref name="code"/> when the on-wire phrase
    /// byte-matches it exactly (no allocation); otherwise allocates the server's exact phrase. This
    /// avoids a per-response string allocation for the overwhelmingly common standard responses while
    /// still surfacing any non-standard phrase verbatim.
    /// </summary>
    public static string ResolveCached(int code, ReadOnlySpan<byte> wirePhrase)
    {
        if (wirePhrase.IsEmpty)
        {
            return string.Empty;
        }

        if (Table.TryGetValue(code, out var canonical) && AsciiEquals(wirePhrase, canonical))
        {
            return canonical;
        }

        return Encoding.ASCII.GetString(wirePhrase);
    }

    private static bool AsciiEquals(ReadOnlySpan<byte> bytes, string text)
    {
        if (bytes.Length != text.Length)
        {
            return false;
        }

        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] != (byte)text[i])
            {
                return false;
            }
        }

        return true;
    }
}