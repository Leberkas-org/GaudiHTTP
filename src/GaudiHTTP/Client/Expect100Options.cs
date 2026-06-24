using GaudiHTTP.Protocol.Semantics;

namespace GaudiHTTP.Client;

/// <summary>
/// Configuration for the <c>Expect: 100-continue</c> handshake applied to large request bodies.
/// Pass to <c>WithExpectContinue</c> on an <see cref="IGaudiHttpClientBuilder"/>.
/// </summary>
public sealed class Expect100Options
{
    /// <summary>
    /// Minimum request body size (in bytes) that triggers the <c>Expect: 100-continue</c> header.
    /// Requests with a body smaller than this threshold pass through unchanged.
    /// Default is 1024.
    /// </summary>
    public long MinBodySize { get; set; } = 1024;

    internal Expect100Policy To() => new()
    {
        MinBodySizeBytes = MinBodySize
    };
}