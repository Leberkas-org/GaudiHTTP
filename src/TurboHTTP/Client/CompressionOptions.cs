using TurboHTTP.Protocol;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Client;

/// <summary>
/// Configuration for request body compression applied by the client before sending.
/// Pass to <c>WithRequestCompression</c> on an <see cref="ITurboHttpClientBuilder"/>.
/// </summary>
public sealed class CompressionOptions
{
    /// <summary>
    /// The content encoding to apply (e.g. "gzip", "deflate", "br").
    /// Default is "gzip".
    /// </summary>
    public string Encoding { get; set; } = WellKnownHeaders.GzipValue;

    /// <summary>
    /// Minimum request body size (in bytes) that triggers compression.
    /// Bodies smaller than this threshold pass through uncompressed.
    /// Default is 1024.
    /// </summary>
    public long MinBodySize { get; set; } = 1024;

    internal CompressionPolicy To() => new()
    {
        Encoding = Encoding,
        MinBodySizeBytes = MinBodySize,
    };
}