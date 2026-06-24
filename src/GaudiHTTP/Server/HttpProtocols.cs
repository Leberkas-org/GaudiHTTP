using System.Net.Security;

namespace TurboHTTP.Server;

/// <summary>
/// Flags enumeration of HTTP protocol versions that a server endpoint may negotiate.
/// Multiple values can be combined; e.g. <see cref="Http1AndHttp2"/> enables both.
/// </summary>
[Flags]
public enum HttpProtocols
{
    /// <summary>No protocol enabled.</summary>
    None = 0,
    /// <summary>HTTP/1.0 and HTTP/1.1.</summary>
    Http1 = 1,
    /// <summary>HTTP/2.</summary>
    Http2 = 2,
    /// <summary>Both HTTP/1.x and HTTP/2 (ALPN negotiated over TLS or via upgrade for cleartext).</summary>
    Http1AndHttp2 = Http1 | Http2,
    /// <summary>HTTP/3 over QUIC (requires HTTPS).</summary>
    Http3 = 4
}

/// <summary>Extension methods for <see cref="HttpProtocols"/>.</summary>
public static class HttpProtocolsExtensions
{
    /// <summary>
    /// Converts the enabled protocol flags to the corresponding ALPN protocol identifiers,
    /// ordered from highest to lowest preference (H3, H2, H1).
    /// </summary>
    public static List<SslApplicationProtocol> ToAlpnProtocols(this HttpProtocols protocols)
    {
        var result = new List<SslApplicationProtocol>();

        if ((protocols & HttpProtocols.Http3) != 0)
        {
            result.Add(SslApplicationProtocol.Http3);
        }

        if ((protocols & HttpProtocols.Http2) != 0)
        {
            result.Add(SslApplicationProtocol.Http2);
        }

        if ((protocols & HttpProtocols.Http1) != 0)
        {
            result.Add(SslApplicationProtocol.Http11);
        }

        return result;
    }
}