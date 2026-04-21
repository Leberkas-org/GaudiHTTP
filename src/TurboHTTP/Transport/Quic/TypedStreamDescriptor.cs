using TurboHTTP.Internal;
using TurboHTTP.Transport.Connection;

namespace TurboHTTP.Transport.Quic;

internal readonly record struct TypedStreamDescriptor(long StreamTypeValue, long SyntheticStreamId, bool Outbound);

internal sealed class TypedStreamState
{
    public ConnectionHandle? Handle;
    public readonly Queue<NetworkBuffer> PendingItems = new();
    public long StreamId;
}
