namespace TurboHTTP.Protocol.Body;

/// <summary>
/// Static helpers for HTTP/1.1 chunked transfer-encoding framing.
/// Writes directly into caller-supplied spans — zero allocation.
/// </summary>
internal static class ChunkedFramingHelper
{
    private static ReadOnlySpan<byte> CrLf => "\r\n"u8;
    private static ReadOnlySpan<byte> Terminator => "0\r\n\r\n"u8;

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
