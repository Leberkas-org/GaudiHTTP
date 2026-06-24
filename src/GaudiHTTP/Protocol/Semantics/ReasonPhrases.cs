using System.Text;

namespace GaudiHTTP.Protocol.Semantics;

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

    private static readonly Dictionary<int, byte[]> BytesTable = new()
    {
        [100] = "Continue"u8.ToArray(),
        [101] = "Switching Protocols"u8.ToArray(),
        [200] = "OK"u8.ToArray(),
        [201] = "Created"u8.ToArray(),
        [202] = "Accepted"u8.ToArray(),
        [204] = "No Content"u8.ToArray(),
        [206] = "Partial Content"u8.ToArray(),
        [301] = "Moved Permanently"u8.ToArray(),
        [302] = "Found"u8.ToArray(),
        [303] = "See Other"u8.ToArray(),
        [304] = "Not Modified"u8.ToArray(),
        [307] = "Temporary Redirect"u8.ToArray(),
        [308] = "Permanent Redirect"u8.ToArray(),
        [400] = "Bad Request"u8.ToArray(),
        [401] = "Unauthorized"u8.ToArray(),
        [403] = "Forbidden"u8.ToArray(),
        [404] = "Not Found"u8.ToArray(),
        [405] = "Method Not Allowed"u8.ToArray(),
        [408] = "Request Timeout"u8.ToArray(),
        [409] = "Conflict"u8.ToArray(),
        [411] = "Length Required"u8.ToArray(),
        [413] = "Content Too Large"u8.ToArray(),
        [414] = "URI Too Long"u8.ToArray(),
        [415] = "Unsupported Media Type"u8.ToArray(),
        [416] = "Range Not Satisfiable"u8.ToArray(),
        [417] = "Expectation Failed"u8.ToArray(),
        [426] = "Upgrade Required"u8.ToArray(),
        [500] = "Internal Server Error"u8.ToArray(),
        [501] = "Not Implemented"u8.ToArray(),
        [502] = "Bad Gateway"u8.ToArray(),
        [503] = "Service Unavailable"u8.ToArray(),
        [504] = "Gateway Timeout"u8.ToArray(),
        [505] = "HTTP Version Not Supported"u8.ToArray()
    };

    public static string For(int code) => Table.GetValueOrDefault(code, "");

    public static ReadOnlySpan<byte> ForBytes(int code)
        => BytesTable.TryGetValue(code, out var bytes) ? bytes : [];

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