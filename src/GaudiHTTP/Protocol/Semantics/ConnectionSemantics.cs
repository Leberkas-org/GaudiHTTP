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
        var hasKeepAlive = false;
        var hasClose = false;

        for (var i = 0; i < headers.Count; i++)
        {
            var entry = headers[i];
            if (!string.Equals(entry.Name, WellKnownHeaders.Connection, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = entry.Value;
            foreach (var part in value.AsSpan().Split(','))
            {
                var t = HeaderValidation.TrimOws(value[part.Start..part.End]);
                if (string.IsNullOrEmpty(t))
                {
                    continue;
                }

                if (string.Equals(t, WellKnownHeaders.KeepAliveValue, StringComparison.OrdinalIgnoreCase))
                {
                    hasKeepAlive = true;
                }
                else if (string.Equals(t, WellKnownHeaders.CloseValue, StringComparison.OrdinalIgnoreCase))
                {
                    hasClose = true;
                }
            }
        }

        if (version.Equals(HttpVersion.Version10))
        {
            return hasKeepAlive;
        }

        if (version.Equals(HttpVersion.Version11))
        {
            return !hasClose;
        }

        return true;
    }
}