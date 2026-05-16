using System.Buffers;

namespace TurboHTTP.Protocol.Multiplexed.Body;

internal sealed record StreamBodyChunk(int StreamId, IMemoryOwner<byte> Owner, int Length);
internal sealed record StreamBodyComplete(int StreamId);
internal sealed record StreamBodyFailed(int StreamId, Exception Reason);
