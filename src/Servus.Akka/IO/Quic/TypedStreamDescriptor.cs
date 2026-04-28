namespace Servus.Akka.IO.Quic;

public sealed class TypedStreamState
{
    public StreamHandle? Handle;
    public readonly Queue<NetworkBuffer> PendingItems = new();
    public long StreamId;
    public long OriginalSyntheticStreamId;
    public bool IsOutbound;
}