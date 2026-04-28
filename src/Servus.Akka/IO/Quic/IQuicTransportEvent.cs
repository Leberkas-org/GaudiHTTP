using System.Net;

namespace Servus.Akka.IO.Quic;

public interface IQuicTransportEvent;

public readonly record struct ConnectionLeaseAcquired(QuicConnectionLease Lease) : IQuicTransportEvent;

public readonly record struct RequestLeaseAcquired(QuicStreamLease Lease, long StreamId) : IQuicTransportEvent;

public readonly record struct TypedLeaseAcquired(QuicStreamLease Lease, long StreamTypeValue, long StreamId) : IQuicTransportEvent;

public readonly record struct AcquisitionFailed(Exception Error) : IQuicTransportEvent;

public readonly record struct InboundData(IInputItem Item, int Gen) : IQuicTransportEvent;

public readonly record struct InboundComplete(QuicCloseKind CloseKind, int Gen, long StreamId) : IQuicTransportEvent;

public readonly record struct InboundPumpFailed(Exception Error, long StreamId) : IQuicTransportEvent;

public readonly record struct InboundStreamReady(InboundStream Stream) : IQuicTransportEvent;

public readonly record struct OutboundWriteDone : IQuicTransportEvent;

public readonly record struct OutboundWriteFailed(Exception Error) : IQuicTransportEvent;

public readonly record struct EarlyDataRejected(NetworkBuffer Buffer) : IQuicTransportEvent;

public readonly record struct ConnectionMigrated(
    EndPoint? OldLocalEndPoint,
    EndPoint? NewLocalEndPoint) : IQuicTransportEvent;
