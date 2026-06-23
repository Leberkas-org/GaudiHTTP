using Microsoft.AspNetCore.Http;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Protocol.Body;

/// <summary>
/// Static helpers for HTTP/1.1 chunked transfer-encoding framing.
/// Writes directly into caller-supplied spans — zero allocation.
/// </summary>
internal static class ChunkedFramingHelper
{
    private static ReadOnlySpan<byte> CrLf => "\r\n"u8;
    private static ReadOnlySpan<byte> Terminator => "0\r\n\r\n"u8;
    private static ReadOnlySpan<byte> LastChunk => "0\r\n"u8;
    private static ReadOnlySpan<byte> ColonSpace => ": "u8;

    /// <summary>
    /// Returns the total byte count needed to frame <paramref name="dataLength"/> bytes:
    /// hex-length digits + CRLF + data + CRLF.
    /// </summary>
    public static int GetFramedSize(int dataLength)
    {
        return CountHexDigits(dataLength) + 2 + dataLength + 2;
    }

    /// <summary>
    /// Writes a single chunk as <c>{hex}\r\n{data}\r\n</c> into <paramref name="destination"/>.
    /// Returns the number of bytes written.
    /// </summary>
    public static int WriteChunk(ReadOnlySpan<byte> data, Span<byte> destination)
    {
        var offset = 0;
        offset += WriteHex(data.Length, destination[offset..]);
        CrLf.CopyTo(destination[offset..]);
        offset += 2;
        data.CopyTo(destination[offset..]);
        offset += data.Length;
        CrLf.CopyTo(destination[offset..]);
        offset += 2;
        return offset;
    }

    /// <summary>
    /// Writes the terminal chunk <c>0\r\n\r\n</c> into <paramref name="destination"/>.
    /// Returns the number of bytes written (always 5).
    /// </summary>
    public static int WriteTerminator(Span<byte> destination)
    {
        Terminator.CopyTo(destination);
        return 5;
    }

    /// <summary>
    /// Writes the last chunk marker <c>0\r\n</c> without the trailing section terminator.
    /// Use with <see cref="WriteTrailerSection"/> to emit trailers between the last chunk and the final CRLF.
    /// Returns the number of bytes written (always 3).
    /// </summary>
    public static int WriteLastChunk(Span<byte> destination)
    {
        LastChunk.CopyTo(destination);
        return 3;
    }

    /// <summary>
    /// Estimates the byte count needed for the trailer section (filtered trailers + final CRLF).
    /// </summary>
    public static int GetTrailerSectionSize(IHeaderDictionary trailers)
    {
        var size = 2; // final CRLF
        foreach (var header in trailers)
        {
            if (!TrailerFieldValidator.IsAllowedInTrailer(header.Key))
            {
                continue;
            }

            // "name: value\r\n"
            size += header.Key.Length + 2 + JoinedValueLength(header.Value) + 2;
        }

        return size;
    }

    /// <summary>
    /// Writes filtered trailer headers followed by a final CRLF into <paramref name="destination"/>.
    /// RFC 9112 §7.1.2: trailer-section = *(field-line CRLF) CRLF.
    /// Returns the number of bytes written.
    /// </summary>
    public static int WriteTrailerSection(Span<byte> destination, IHeaderDictionary trailers)
    {
        var offset = 0;

        foreach (var header in trailers)
        {
            if (!TrailerFieldValidator.IsAllowedInTrailer(header.Key))
            {
                continue;
            }

            var name = header.Key;
            var values = header.Value;

            for (var i = 0; i < name.Length; i++)
            {
                destination[offset++] = (byte)name[i];
            }

            ColonSpace.CopyTo(destination[offset..]);
            offset += 2;

            for (var v = 0; v < values.Count; v++)
            {
                if (v > 0)
                {
                    destination[offset++] = (byte)',';
                    destination[offset++] = (byte)' ';
                }

                var val = values[v] ?? string.Empty;
                for (var i = 0; i < val.Length; i++)
                {
                    destination[offset++] = (byte)val[i];
                }
            }

            CrLf.CopyTo(destination[offset..]);
            offset += 2;
        }

        CrLf.CopyTo(destination[offset..]);
        offset += 2;

        return offset;
    }

    private static int JoinedValueLength(Microsoft.Extensions.Primitives.StringValues values)
    {
        if (values.Count <= 1)
        {
            return values.ToString().Length;
        }

        var length = 0;
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                length += 2; // ", "
            }

            length += values[i]?.Length ?? 0;
        }

        return length;
    }

    private static int WriteHex(int value, Span<byte> destination)
    {
        if (value == 0)
        {
            destination[0] = (byte)'0';
            return 1;
        }

        var length = CountHexDigits(value);
        var pos = length;
        var v = value;

        while (v != 0)
        {
            var digit = v & 0xF;
            destination[--pos] = (byte)(digit < 10 ? '0' + digit : 'a' + (digit - 10));
            v >>= 4;
        }

        return length;
    }

    private static int CountHexDigits(int value)
    {
        return value switch
        {
            0 => 1,
            <= 0xF => 1,
            <= 0xFF => 2,
            <= 0xFFF => 3,
            <= 0xFFFF => 4,
            <= 0xFFFFF => 5,
            <= 0xFFFFFF => 6,
            <= 0xFFFFFFF => 7,
            _ => 8
        };
    }
}
