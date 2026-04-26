using System.Threading.Channels;
using Akka.Actor;
using Servus.Akka.IO.Tcp;

namespace Servus.Akka.IO;

public sealed record ConnectionHandle(
    ChannelWriter<IoBuffer> OutboundWriter,
    ChannelReader<IoBuffer> InboundReader,
    RequestEndpoint Key,
    IActorRef ConnectionActor)
{
    public int MaxConcurrentStreams { get; private set; } = 100;

    public void UpdateMaxConcurrentStreams(int value) => MaxConcurrentStreams = value;

    public TlsCloseKind CloseKind { get; private set; }

    public void SetCloseKind(TlsCloseKind value) => CloseKind = value;

    public ValueTask WriteAsync(NetworkBuffer buffer)
    {
        return OutboundWriter.WriteAsync(buffer.DetachAsIoBuffer());
    }

    public bool TryCompleteOutbound(Exception? error = null)
    {
        return OutboundWriter.TryComplete(error);
    }

    public static ConnectionHandle CreateDirect(
        ChannelWriter<IoBuffer> outboundWriter,
        ChannelReader<IoBuffer> inboundReader,
        RequestEndpoint key)
    {
        return new ConnectionHandle(outboundWriter, inboundReader, key, ActorRefs.Nobody);
    }

    public bool Equals(ConnectionHandle? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return EqualityContract == other.EqualityContract
            && EqualityComparer<ChannelWriter<IoBuffer>>.Default.Equals(OutboundWriter, other.OutboundWriter)
            && EqualityComparer<ChannelReader<IoBuffer>>.Default.Equals(InboundReader, other.InboundReader)
            && Key.Equals(other.Key)
            && EqualityComparer<IActorRef>.Default.Equals(ConnectionActor, other.ConnectionActor);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(EqualityContract, OutboundWriter, InboundReader, Key, ConnectionActor);
    }
}
