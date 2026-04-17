using System.IO.Compression;

namespace TurboHTTP.Protocol.Semantics;

/// <summary>
/// RFC 9110 §8.4 — Content-Encoding decompression for HTTP responses.
/// Handles gzip, deflate, br (Brotli), and identity encodings.
/// For stacked encodings (e.g. "gzip, br"), decodes in reverse order
/// (outermost encoding decoded first).
/// </summary>
internal static class ContentEncoding
{
    /// <summary>
    /// Returns <see langword="true"/> when the given encoding token is one this decoder can handle
    /// (gzip, x-gzip, deflate, br, identity, or empty/null).
    /// </summary>
    public static bool IsSupported(string? encoding)
    {
        if (string.IsNullOrWhiteSpace(encoding))
        {
            return true;
        }

        // A comma-separated list may contain multiple tokens; all must be supported.
        var tokens = encoding.Split(',');

        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i].Trim();

            if (string.IsNullOrEmpty(token) ||
                token.Equals(WellKnownHeaders.Identity, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!IsSupportedToken(token))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSupportedToken(string token)
    {
        return token.Equals(WellKnownHeaders.Gzip, StringComparison.OrdinalIgnoreCase) ||
               token.Equals(WellKnownHeaders.XGzip, StringComparison.OrdinalIgnoreCase) ||
               token.Equals(WellKnownHeaders.Deflate, StringComparison.OrdinalIgnoreCase) ||
               token.Equals(WellKnownHeaders.Brotli, StringComparison.OrdinalIgnoreCase);
    }

    internal static Stream CreateDecompressor(Stream source, string encoding)
        => CreateCodecStream(source, encoding, CompressionMode.Decompress);

    internal static Stream CreateCompressor(Stream source, string encoding)
        => CreateCodecStream(source, encoding, CompressionMode.Compress, true);

    internal static Stream CreateCodecStream(Stream stream, string encoding, CompressionMode mode,
        bool leaveOpen = false)
    {
        if (encoding.Equals(WellKnownHeaders.Gzip, StringComparison.OrdinalIgnoreCase) ||
            encoding.Equals(WellKnownHeaders.XGzip, StringComparison.OrdinalIgnoreCase))
        {
            return new GZipStream(stream, mode, leaveOpen);
        }

        if (encoding.Equals(WellKnownHeaders.Brotli, StringComparison.OrdinalIgnoreCase))
        {
            return new BrotliStream(stream, mode, leaveOpen);
        }

        if (encoding.Equals(WellKnownHeaders.Deflate, StringComparison.OrdinalIgnoreCase))
        {
            return new ZLibStream(stream, mode, leaveOpen);
        }

        throw new HttpDecoderException(HttpDecoderError.DecompressionFailed,
            $"RFC 9110 §8.4: Unknown Content-Encoding '{encoding}'.");
    }
}