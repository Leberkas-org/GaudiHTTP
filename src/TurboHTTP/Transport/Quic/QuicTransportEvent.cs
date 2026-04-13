using TurboHTTP.Internal;
using TurboHTTP.Transport.Connection;

namespace TurboHTTP.Transport.Quic;

/// <summary>
/// Discriminated union of all async events that arrive from outside the stage thread.
/// Dispatched via <see cref="QuicTransportStateMachine.Dispatch"/>.
/// </summary>
internal abstract record QuicTransportEvent
{
    /// <summary>The actor granted a QUIC connection lease (connection-level, before stream open).</summary>
    internal sealed record ConnectionLeaseAcquired(QuicConnectionLease Lease) : QuicTransportEvent;

    internal sealed record RequestLeaseAcquired(ConnectionLease Lease, long StreamId) : QuicTransportEvent;

    internal sealed record TypedLeaseAcquired(ConnectionLease Lease, OutputStreamType StreamType) : QuicTransportEvent;

    internal sealed record AcquisitionFailed(Exception Error) : QuicTransportEvent;

    internal sealed record InboundData(IInputItem Item, int Gen) : QuicTransportEvent;

    internal sealed record InboundComplete(TlsCloseKind CloseKind, int Gen, long StreamId = -1) : QuicTransportEvent;

    internal sealed record InboundPumpFailed(Exception Error, long StreamId = -1) : QuicTransportEvent;

    internal sealed record InboundStreamReady(QuicConnectionHandle.InboundStream Stream) : QuicTransportEvent;

    internal sealed record OutboundWriteDone : QuicTransportEvent;

    internal sealed record OutboundWriteFailed(Exception Error) : QuicTransportEvent;

    /// <summary>
    /// Raised when a QUIC 0-RTT early data write was rejected by the server.
    /// The transport should re-send the buffered data after full handshake completes.
    /// </summary>
    internal sealed record EarlyDataRejected(NetworkBuffer Buffer) : QuicTransportEvent;

    /// <summary>
    /// Raised when the QUIC connection's local address has changed (connection migration).
    /// The transport checks <see cref="QuicOptions.AllowConnectionMigration"/> to decide
    /// whether to continue transparently or close and reconnect. RFC 9000 §9.
    /// </summary>
    internal sealed record ConnectionMigrated(System.Net.EndPoint? OldLocalEndPoint, System.Net.EndPoint? NewLocalEndPoint) : QuicTransportEvent;
}
