using System.Buffers;
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

    public static byte[] Decode(ReadOnlySpan<byte> data, ref int pos, int prefixBits)
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
            return [];
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
            using var owner = MemoryPool<byte>.Shared.Rent(maxDecoded);
            var decodedLen = HuffmanCodec.Decode(raw, owner.Memory.Span[..maxDecoded]);
            return owner.Memory.Span[..decodedLen].ToArray();
        }

        return raw.ToArray();
    }

    public static string DecodeToString(ReadOnlySpan<byte> data, ref int pos, int prefixBits)
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
            using var owner = MemoryPool<byte>.Shared.Rent(maxDecoded);
            var decoded = owner.Memory.Span[..maxDecoded];
            var decodedLen = HuffmanCodec.Decode(raw, decoded);
            var result = decoded[..decodedLen];
            return WellKnownHeaders.TryResolve(result, out var cached)
                ? cached
                : Encoding.UTF8.GetString(result);
        }

        return WellKnownHeaders.TryResolve(raw, out var known)
            ? known
            : Encoding.UTF8.GetString(raw);
    }
}
