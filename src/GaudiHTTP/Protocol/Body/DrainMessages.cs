namespace GaudiHTTP.Protocol.Body;

internal readonly record struct DrainReadComplete<TStreamId>(TStreamId StreamId, int BytesRead);
internal readonly record struct DrainReadFailed<TStreamId>(TStreamId StreamId, Exception Reason);
internal readonly record struct DrainContinue(int StreamId);

internal readonly record struct MultiplexedDrainReadComplete(long StreamId, int BytesRead);
internal readonly record struct MultiplexedDrainReadFailed(long StreamId, Exception Reason);
internal readonly record struct MultiplexedDrainContinue(long StreamId);

internal sealed class ContinueDrain
{
    public static readonly ContinueDrain Instance = new();
    private ContinueDrain() { }
}
