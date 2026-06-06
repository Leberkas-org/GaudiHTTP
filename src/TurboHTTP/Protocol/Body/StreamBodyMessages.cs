using System.Buffers;

namespace TurboHTTP.Protocol.Body;

internal sealed record StreamBodyChunk(
    IMemoryOwner<byte> Owner,
    int Length,
    int Offset = 0)
{
    public ReadOnlyMemory<byte> Data => Owner.Memory.Slice(Offset, Length);
}