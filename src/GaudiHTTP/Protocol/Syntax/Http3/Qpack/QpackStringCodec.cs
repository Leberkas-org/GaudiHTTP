using System.Text;

namespace TurboHTTP.Protocol.Syntax.Http3.Qpack;

internal static class QpackStringCodec
{
    public static int Encode(ReadOnlySpan<byte> value, int prefixBits, byte prefixFlags, ref SpanWriter writer)
    {
        if (value.IsEmpty)
        {
            return QpackIntegerCodec.Encode(0, prefixBits, prefixFlags, ref writer);
        }

        var huffLen = HuffmanCodec.GetEncodedLength(value);

        if (huffLen < value.Length)
        {
            var hBit = (byte)(1 << prefixBits);
            var written = QpackIntegerCodec.Encode(huffLen, prefixBits, (byte)(prefixFlags | hBit), ref writer);
            var actualHuffLen = HuffmanCodec.Encode(value, writer.Remaining[..huffLen]);
            writer.Advance(actualHuffLen);
            return written + actualHuffLen;
        }

        var n = QpackIntegerCodec.Encode(value.Length, prefixBits, prefixFlags, ref writer);
        value.CopyTo(writer.Remaining);
        writer.Advance(value.Length);
        return n + value.Length;
    }

    public static int Encode(ReadOnlySpan<byte> value, int prefixBits, byte prefixFlags, bool useHuffman, ref SpanWriter writer)
    {
        if (value.IsEmpty)
        {
            var flags = useHuffman ? (byte)(prefixFlags | (1 << prefixBits)) : prefixFlags;
            return QpackIntegerCodec.Encode(0, prefixBits, flags, ref writer);
        }

        if (useHuffman)
        {
            var huffLen = HuffmanCodec.GetEncodedLength(value);
            var hBit = (byte)(1 << prefixBits);
            var written = QpackIntegerCodec.Encode(huffLen, prefixBits, (byte)(prefixFlags | hBit), ref writer);
            var actualHuffLen = HuffmanCodec.Encode(value, writer.Remaining[..huffLen]);
            writer.Advance(actualHuffLen);
            return written + actualHuffLen;
        }

        var n = QpackIntegerCodec.Encode(value.Length, prefixBits, prefixFlags, ref writer);
        value.CopyTo(writer.Remaining);
        writer.Advance(value.Length);
        return n + value.Length;
    }

    public static string DecodeToString(ReadOnlySpan<byte> data, ref int pos, int prefixBits)
    {
        byte[] scratch = [];
        return DecodeToString(data, ref pos, prefixBits, ref scratch);
    }

    public static string DecodeToString(ReadOnlySpan<byte> data, ref int pos, int prefixBits, ref byte[] scratch)
    {
        if (pos >= data.Length)
        {
            throw new QpackException("RFC 9204 §4.1.2 violation: Unexpected end of data while reading string literal.");
        }

        var hBit = (byte)(1 << prefixBits);
        var isHuffman = (data[pos] & hBit) != 0;

        var length = QpackIntegerCodec.Decode(data, ref pos, prefixBits);

        if (length == 0)
        {
            return string.Empty;
        }

        if (pos + length > data.Length)
        {
            throw new QpackException(
                $"RFC 9204 §4.1.2 violation: String literal length {length} exceeds available data ({data.Length - pos} bytes remaining).");
        }

        var raw = data.Slice(pos, length);
        pos += length;

        if (isHuffman)
        {
            var maxDecoded = HuffmanCodec.GetMaxDecodedLength(raw.Length);
            if (maxDecoded > scratch.Length)
            {
                scratch = new byte[maxDecoded];
            }

            var decodedLen = HuffmanCodec.Decode(raw, scratch.AsSpan(0, maxDecoded));
            var result = scratch.AsSpan(0, decodedLen);
            return WellKnownHeaders.TryResolve(result, out var cached)
                ? cached
                : Encoding.UTF8.GetString(result);
        }

        return WellKnownHeaders.TryResolve(raw, out var known)
            ? known
            : Encoding.UTF8.GetString(raw);
    }

    public static string DecodeToString(ReadOnlySpan<byte> data, ref int pos, int prefixBits, ref byte[] scratch, HeaderNameCache nameCache)
    {
        if (pos >= data.Length)
        {
            throw new QpackException("RFC 9204 §4.1.2 violation: Unexpected end of data while reading string literal.");
        }

        var hBit = (byte)(1 << prefixBits);
        var isHuffman = (data[pos] & hBit) != 0;

        var length = QpackIntegerCodec.Decode(data, ref pos, prefixBits);

        if (length == 0)
        {
            return string.Empty;
        }

        if (pos + length > data.Length)
        {
            throw new QpackException(
                $"RFC 9204 §4.1.2 violation: String literal length {length} exceeds available data ({data.Length - pos} bytes remaining).");
        }

        var raw = data.Slice(pos, length);
        pos += length;

        if (isHuffman)
        {
            var maxDecoded = HuffmanCodec.GetMaxDecodedLength(raw.Length);
            if (maxDecoded > scratch.Length)
            {
                scratch = new byte[maxDecoded];
            }

            var decodedLen = HuffmanCodec.Decode(raw, scratch.AsSpan(0, maxDecoded));
            return nameCache.GetOrAdd(scratch.AsSpan(0, decodedLen));
        }

        return nameCache.GetOrAdd(raw);
    }
}
