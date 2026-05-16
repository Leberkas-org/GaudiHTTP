using System.Net;

namespace TurboHTTP.Protocol.Semantics;

internal static class ConnectionSemantics
{
    private static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        WellKnownHeaders.Connection,
        WellKnownHeaders.KeepAliveHeader,
        WellKnownHeaders.TransferEncoding,
        WellKnownHeaders.Te,
        WellKnownHeaders.Upgrade,
        WellKnownHeaders.ProxyAuthenticate,
        WellKnownHeaders.ProxyAuthorization,
        WellKnownHeaders.Trailer
    };

    public static bool IsHopByHop(string headerName) => HopByHopHeaders.Contains(headerName);

    public static bool IsPersistent(HeaderCollection headers, Version version)
    {
        var tokens = new List<string>();
        foreach (var v in headers.GetValues(WellKnownHeaders.Connection))
        {
            foreach (var part in v.AsSpan().Split(','))
            {
                var t = HeaderValidation.TrimOws(v[part.Start..part.End]);
                if (!string.IsNullOrEmpty(t))
                {
                    tokens.Add(t);
                }
            }
        }

        if (version.Equals(HttpVersion.Version10))
        {
            return Has(WellKnownHeaders.KeepAliveValue);
        }

        if (version.Equals(HttpVersion.Version11))
        {
            return !Has(WellKnownHeaders.CloseValue);
        }

        return true;

        bool Has(string needle)
        {
            for (var i = 0; i < tokens.Count; i++)
            {
                if (string.Equals(tokens[i], needle, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}