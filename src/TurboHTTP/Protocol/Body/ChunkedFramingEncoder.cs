using System.Buffers;

namespace TurboHTTP.Protocol.Body;

internal sealed class ChunkedFramingEncoder(int maxChunkSize) : IFramingEncoder
{
    public int Headroom { get; } = HexDigitCount(maxChunkSize) + 2;
    public int Trailer => 2;

    public ReadOnlyMemory<byte> Frame(IMemoryOwner<byte> buffer, int headroom, int dataLength)
    {
        var actualHexLen = HexDigitCount(dataLength);
        var chunkStart = headroom - actualHexLen - 2;

        var headerWriter = SpanWriter.Create(buffer.Memory.Span[chunkStart..]);
        headerWriter.WriteHex(dataLength);
        headerWriter.WriteCrlf();

        var trailerWriter = SpanWriter.Create(buffer.Memory.Span[(headroom + dataLength)..]);
        trailerWriter.WriteCrlf();

        var chunkLen = actualHexLen + 2 + dataLength + 2;
        return buffer.Memory.Slice(chunkStart, chunkLen);
    }

    public OwnedMemory GetTerminator()
    {
        var owner = MemoryPool<byte>.Shared.Rent(5);
        var writer = SpanWriter.Create(owner.Memory.Span);
        writer.WriteBytes(WellKnownHeaders.ZeroValue);
        writer.WriteCrlf();
        writer.WriteCrlf();
        return new OwnedMemory(owner, owner.Memory[..writer.BytesWritten]);
    }

    private static int HexDigitCount(int value)
    {
        return value switch
        {
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
