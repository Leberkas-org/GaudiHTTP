namespace GaudiHTTP.Protocol.Syntax.Http3.Qpack;

internal static class QpackIntegerCodec
{
    private const int MaxIntegerValue = int.MaxValue >> 1;

    public static int Encode(int value, int prefixBits, byte prefixFlags, ref SpanWriter writer)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Value must be non-negative.");
        }

        if (prefixBits is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(prefixBits), "prefixBits must be between 1 and 8.");
        }

        var mask = (1 << prefixBits) - 1;

        if (value < mask)
        {
            writer.Remaining[0] = (byte)(prefixFlags | value);
            writer.Advance(1);
            return 1;
        }

        writer.Remaining[0] = (byte)(prefixFlags | mask);
        writer.Advance(1);
        var written = 1;

        var remaining = value - mask;

        while (remaining >= 0x80)
        {
            writer.Remaining[0] = (byte)((remaining & 0x7F) | 0x80);
            writer.Advance(1);
            remaining >>= 7;
            written++;
        }

        writer.Remaining[0] = (byte)remaining;
        writer.Advance(1);
        written++;

        return written;
    }

    public static int Decode(ReadOnlySpan<byte> data, ref int pos, int prefixBits)
    {
        if (prefixBits is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(prefixBits), "prefixBits must be between 1 and 8.");
        }

        if (pos >= data.Length)
        {
            throw new QpackException("RFC 9204 §4.1.1 violation: Unexpected end of data while reading integer.");
        }

        var mask = (1 << prefixBits) - 1;
        var value = data[pos] & mask;
        pos++;

        if (value < mask)
        {
            return value;
        }

        var shift = 0;
        long lvalue = value;

        while (true)
        {
            if (pos >= data.Length)
            {
                throw new QpackException("RFC 9204 §4.1.1 violation: Integer is truncated (no stop bit found).");
            }

            if (shift >= 62)
            {
                throw new QpackException("RFC 9204 §4.1.1 violation: Integer overflow - encoding length exceeded.");
            }

            var b = data[pos++];
            lvalue += (long)(b & 0x7F) << shift;
            shift += 7;

            if (lvalue > MaxIntegerValue)
            {
                throw new QpackException(
                    $"RFC 9204 §4.1.1 violation: Integer overflow - value {lvalue} exceeds maximum {MaxIntegerValue}.");
            }

            if ((b & 0x80) == 0)
            {
                return (int)lvalue;
            }
        }
    }
}
