using System.Buffers;

namespace TurboHTTP.Protocol.Multiplexed.Body;

internal sealed record StreamBodyChunk<T>(T StreamId, IMemoryOwner<byte> Owner, int Length, int Offset = 0, int Headroom = 0)
{
    public ReadOnlyMemory<byte> Data => Owner.Memory.Slice(Offset, Length);
}

internal readonly record struct StreamBodyComplete<T>(T StreamId);

internal readonly record struct StreamBodyFailed<T>(T StreamId, Exception Reason);