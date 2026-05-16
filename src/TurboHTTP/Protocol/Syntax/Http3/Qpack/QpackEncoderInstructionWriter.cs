using System.Text;

namespace TurboHTTP.Protocol.Syntax.Http3.Qpack;

internal static class QpackEncoderInstructionWriter
{
    public static int WriteSetDynamicTableCapacity(int capacity, ref SpanWriter writer)
    {
        if (capacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be non-negative.");
        }

        return QpackIntegerCodec.Encode(capacity, 5, 0x20, ref writer);
    }

    public static int WriteInsertWithNameReference(int nameIndex, bool isStatic, ReadOnlySpan<byte> value, ref SpanWriter writer)
    {
        if (nameIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(nameIndex), "Name index must be non-negative.");
        }

        var total = 0;

        var prefixFlags = (byte)(0x80 | (isStatic ? 0x40 : 0x00));
        total += QpackIntegerCodec.Encode(nameIndex, 6, prefixFlags, ref writer);
        total += QpackStringCodec.Encode(value, 7, 0x00, ref writer);

        return total;
    }

    public static int WriteInsertWithNameReference(int nameIndex, bool isStatic, string value, ref SpanWriter writer)
    {
        var rawLength = Encoding.UTF8.GetByteCount(value);
        if (rawLength == 0)
        {
            return WriteInsertWithNameReference(nameIndex, isStatic, ReadOnlySpan<byte>.Empty, ref writer);
        }

        var utf8Start = writer.Remaining.Length - rawLength;
        Encoding.UTF8.GetBytes(value.AsSpan(), writer.Remaining[utf8Start..]);
        return WriteInsertWithNameReference(nameIndex, isStatic, writer.Remaining.Slice(utf8Start, rawLength), ref writer);
    }

    private static int WriteInsertWithLiteralName(ReadOnlySpan<byte> name, ReadOnlySpan<byte> value, ref SpanWriter writer)
    {
        var total = 0;

        total += QpackStringCodec.Encode(name, 5, 0x40, ref writer);
        total += QpackStringCodec.Encode(value, 7, 0x00, ref writer);

        return total;
    }

    public static int WriteInsertWithLiteralName(string name, string value, ref SpanWriter writer)
    {
        var nameLen = Encoding.UTF8.GetByteCount(name);
        var valueLen = Encoding.UTF8.GetByteCount(value);
        var totalUtf8 = nameLen + valueLen;

        if (totalUtf8 == 0)
        {
            return WriteInsertWithLiteralName(ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty, ref writer);
        }

        var utf8Start = writer.Remaining.Length - totalUtf8;
        var utf8Region = writer.Remaining[utf8Start..];
        Encoding.UTF8.GetBytes(name.AsSpan(), utf8Region[..nameLen]);
        Encoding.UTF8.GetBytes(value.AsSpan(), utf8Region[nameLen..]);

        return WriteInsertWithLiteralName(
            utf8Region[..nameLen],
            utf8Region.Slice(nameLen, valueLen),
            ref writer);
    }

    public static int WriteDuplicate(int index, ref SpanWriter writer)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "Index must be non-negative.");
        }

        return QpackIntegerCodec.Encode(index, 5, 0x00, ref writer);
    }
}
