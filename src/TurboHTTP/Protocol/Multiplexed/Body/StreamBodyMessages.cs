using System.Buffers;

namespace TurboHTTP.Protocol.Multiplexed.Body;

internal sealed record StreamBodyChunk<T>(T StreamId, IMemoryOwner<byte> Owner, int Length);

internal sealed record StreamBodyComplete<T>(T StreamId);

internal sealed record StreamBodyFailed<T>(T StreamId, Exception Reason);