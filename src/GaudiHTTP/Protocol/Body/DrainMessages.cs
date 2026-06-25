namespace GaudiHTTP.Protocol.Body;

internal readonly record struct DrainReadComplete(int StreamId, int BytesRead);
internal readonly record struct DrainReadFailed(int StreamId, Exception Reason);
internal readonly record struct DrainContinue(int StreamId);

internal readonly record struct MultiplexedDrainReadComplete(long StreamId, int BytesRead);
internal readonly record struct MultiplexedDrainReadFailed(long StreamId, Exception Reason);
internal readonly record struct MultiplexedDrainContinue(long StreamId);
