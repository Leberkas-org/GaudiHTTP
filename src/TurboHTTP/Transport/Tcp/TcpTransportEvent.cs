using TurboHTTP.Internal;
using TurboHTTP.Transport.Connection;

namespace TurboHTTP.Transport.Tcp;

/// <summary>
/// Discriminated union of all async events that arrive from outside the stage thread.
/// Dispatched via <see cref="TcpTransportStateMachine.Dispatch"/>.
/// </summary>
internal interface ITcpTransportEvent
{
    internal readonly record struct LeaseAcquired(ConnectionLease Lease) : ITcpTransportEvent;

    internal readonly record struct AcquisitionFailed(Exception Error) : ITcpTransportEvent;

    internal readonly record struct InboundBatch(IInputItem[] Batch, int Count, int Gen) : ITcpTransportEvent;

    internal readonly record struct InboundComplete(TlsCloseKind CloseKind, int Gen) : ITcpTransportEvent;

    internal readonly record struct InboundPumpFailed(Exception Error) : ITcpTransportEvent;

    internal readonly record struct OutboundWriteDone : ITcpTransportEvent;

    internal readonly record struct OutboundWriteFailed(Exception Error) : ITcpTransportEvent;

    internal readonly record struct FlushNextCompleted : ITcpTransportEvent;
}