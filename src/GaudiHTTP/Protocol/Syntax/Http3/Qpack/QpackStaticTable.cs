using System.Collections.Frozen;
using System.Text;

namespace GaudiHTTP.Protocol.Syntax.Http3.Qpack;

/// <summary>
/// RFC 9204 Appendix A - QPACK Static Table.
/// 99 predefined header entries at indices 0-98.
/// Unlike HPACK, QPACK uses 0-based indexing.
/// </summary>
internal static class QpackStaticTable
{
    public const int Count = 99;

    /// Pre-computed UTF-8 byte length of each static table entry's name.
    internal static readonly int[] NameByteLengths;

    /// Pre-computed RFC 9204 §3.2.1 encoded size (nameBytes + valueBytes + 32) for each static entry.
    internal static readonly int[] EncodedSizes;

    /// <summary>
    /// All 99 static table entries indexed 0-98 per RFC 9204 Appendix A.
    /// </summary>
    public static readonly (string Name, string Value)[] Entries =
    [
        (WellKnownHeaders.Authority, string.Empty), // [0]
        (WellKnownHeaders.Path, "/"), // [1]
        ("age", "0"), // [2]
        ("content-disposition", string.Empty), // [3]
        ("content-length", "0"), // [4]
        ("cookie", string.Empty), // [5]
        ("date", string.Empty), // [6]
        ("etag", string.Empty), // [7]
        ("if-modified-since", string.Empty), // [8]
        ("if-none-match", string.Empty), // [9]
        ("last-modified", string.Empty), // [10]
        ("link", string.Empty), // [11]
        ("location", string.Empty), // [12]
        ("referer", string.Empty), // [13]
        ("set-cookie", string.Empty), // [14]
        (WellKnownHeaders.Method, "CONNECT"), // [15]
        (WellKnownHeaders.Method, "DELETE"), // [16]
        (WellKnownHeaders.Method, "GET"), // [17]
        (WellKnownHeaders.Method, "HEAD"), // [18]
        (WellKnownHeaders.Method, "OPTIONS"), // [19]
        (WellKnownHeaders.Method, "POST"), // [20]
        (WellKnownHeaders.Method, "PUT"), // [21]
        (WellKnownHeaders.Scheme, "http"), // [22]
        (WellKnownHeaders.Scheme, "https"), // [23]
        (WellKnownHeaders.Status, "103"), // [24]
        (WellKnownHeaders.Status, "200"), // [25]
        (WellKnownHeaders.Status, "304"), // [26]
        (WellKnownHeaders.Status, "404"), // [27]
        (WellKnownHeaders.Status, "503"), // [28]
        ("accept", "*/*"), // [29]
        ("accept", "application/dns-message"), // [30]
        ("accept-encoding", "gzip, deflate, br"), // [31]
        ("accept-ranges", "bytes"), // [32]
        ("access-control-allow-headers", "cache-control"), // [33]
        ("access-control-allow-headers", "content-type"), // [34]
        ("access-control-allow-origin", "*"), // [35]
        ("cache-control", "max-age=0"), // [36]
        ("cache-control", "max-age=2592000"), // [37]
        ("cache-control", "max-age=604800"), // [38]
        ("cache-control", WellKnownHeaders.NoCache), // [39]
        ("cache-control", "no-store"), // [40]
        ("cache-control", "public, max-age=31536000"), // [41]
        ("content-encoding", WellKnownHeaders.BrValue), // [42]
        ("content-encoding", "gzip"), // [43]
        ("content-type", "application/dns-message"), // [44]
        ("content-type", "application/javascript"), // [45]
        ("content-type", "application/json"), // [46]
        ("content-type", "application/x-www-form-urlencoded"), // [47]
        ("content-type", "image/gif"), // [48]
        ("content-type", "image/jpeg"), // [49]
        ("content-type", "image/png"), // [50]
        ("content-type", "text/css"), // [51]
        ("content-type", "text/html; charset=utf-8"), // [52]
        ("content-type", "text/plain"), // [53]
        ("content-type", "text/plain;charset=utf-8"), // [54]
        ("range", "bytes=0-"), // [55]
        ("strict-transport-security", "max-age=31536000"), // [56]
        ("strict-transport-security", "max-age=31536000; includesubdomains"), // [57]
        ("strict-transport-security", "max-age=31536000; includesubdomains; preload"), // [58]
        ("vary", "accept-encoding"), // [59]
        ("vary", "origin"), // [60]
        ("x-content-type-options", "nosniff"), // [61]
        ("x-xss-protection", "1; mode=block"), // [62]
        (WellKnownHeaders.Status, "100"), // [63]
        (WellKnownHeaders.Status, "204"), // [64]
        (WellKnownHeaders.Status, "206"), // [65]
        (WellKnownHeaders.Status, "302"), // [66]
        (WellKnownHeaders.Status, "400"), // [67]
        (WellKnownHeaders.Status, "403"), // [68]
        (WellKnownHeaders.Status, "421"), // [69]
        (WellKnownHeaders.Status, "425"), // [70]
        (WellKnownHeaders.Status, "500"), // [71]
        ("accept-language", string.Empty), // [72]
        ("access-control-allow-credentials", "FALSE"), // [73]
        ("access-control-allow-credentials", "TRUE"), // [74]
        ("access-control-allow-headers", "*"), // [75]
        ("access-control-allow-methods", "get"), // [76]
        ("access-control-allow-methods", "get, post, options"), // [77]
        ("access-control-allow-methods", "options"), // [78]
        ("access-control-expose-headers", "content-length"), // [79]
        ("access-control-request-headers", "content-type"), // [80]
        ("access-control-request-method", "get"), // [81]
        ("access-control-request-method", "post"), // [82]
        ("alt-svc", "clear"), // [83]
        ("authorization", string.Empty), // [84]
        ("content-security-policy", "script-src 'none'; object-src 'none'; base-uri 'none'"), // [85]
        ("early-data", "1"), // [86]
        ("expect-ct", string.Empty), // [87]
        ("forwarded", string.Empty), // [88]
        ("if-range", string.Empty), // [89]
        ("origin", string.Empty), // [90]
        ("purpose", "prefetch"), // [91]
        ("server", string.Empty), // [92]
        ("timing-allow-origin", "*"), // [93]
        ("upgrade-insecure-requests", "1"), // [94]
        ("user-agent", string.Empty), // [95]
        ("x-forwarded-for", string.Empty), // [96]
        ("x-frame-options", "deny"), // [97]
        ("x-frame-options", "sameorigin") // [98]
    ];

    /// <summary>
    /// Lookup map from (name, value) to static table index for exact matches.
    /// </summary>
    private static readonly FrozenDictionary<(string Name, string Value), int> ExactIndex =
        BuildExactIndex();

    /// <summary>
    /// Lookup map from name to first static table index for name-only matches.
    /// When multiple entries share a name, returns the lowest index.
    /// </summary>
    private static readonly FrozenDictionary<string, int> NameIndex =
        BuildNameIndex();

    /// <summary>
    /// Tries to find an exact (name, value) match in the static table.
    /// </summary>
    /// <returns>The index if found, or -1 if not found.</returns>
    public static int FindExact(string name, string value)
    {
        return ExactIndex.GetValueOrDefault((name, value), -1);
    }

    /// <summary>
    /// Tries to find a name-only match in the static table.
    /// Returns the lowest index for the given name.
    /// </summary>
    /// <returns>The index if found, or -1 if not found.</returns>
    public static int FindName(string name)
    {
        return NameIndex.GetValueOrDefault(name, -1);
    }

    private static FrozenDictionary<(string Name, string Value), int> BuildExactIndex()
    {
        var dict = new Dictionary<(string, string), int>(Count);
        for (var i = 0; i < Count; i++)
        {
            var entry = Entries[i];
            dict.TryAdd((entry.Name, entry.Value), i);
        }

        return dict.ToFrozenDictionary();
    }

    static QpackStaticTable()
    {
        NameByteLengths = new int[Count];
        EncodedSizes = new int[Count];

        for (var i = 0; i < Count; i++)
        {
            var nameBytes = Encoding.UTF8.GetByteCount(Entries[i].Name);
            var valueBytes = Encoding.UTF8.GetByteCount(Entries[i].Value);
            NameByteLengths[i] = nameBytes;
            EncodedSizes[i] = nameBytes + valueBytes + 32;
        }
    }

    private static FrozenDictionary<string, int> BuildNameIndex()
    {
        var dict = new Dictionary<string, int>(Count);
        for (var i = 0; i < Count; i++)
        {
            dict.TryAdd(Entries[i].Name, i);
        }

        return dict.ToFrozenDictionary();
    }
}
