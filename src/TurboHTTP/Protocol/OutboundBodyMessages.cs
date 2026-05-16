using System.Buffers;

namespace TurboHTTP.Protocol;

internal sealed record OutboundBodyChunk(IMemoryOwner<byte> Owner, int Length);
internal sealed record OutboundBodyComplete;
internal sealed record OutboundBodyFailed(Exception Reason);
