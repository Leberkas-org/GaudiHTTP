namespace GaudiHTTP.Protocol.Body;

internal readonly record struct DrainReadComplete<TStreamId>(TStreamId StreamId, int BytesRead);
internal readonly record struct DrainReadFailed<TStreamId>(TStreamId StreamId, Exception Reason);

internal sealed class ContinueDrain
{
    public static readonly ContinueDrain Instance = new();
    private ContinueDrain() { }
}
