namespace Servus.Akka.IO.Tcp;

public readonly record struct LeaseAcquired(ConnectionLease Lease) : ITcpTransportEvent;

public readonly record struct AcquisitionFailed(Exception Error) : ITcpTransportEvent;

public readonly record struct InboundBatch(IInputItem[] Batch, int Count, int Gen) : ITcpTransportEvent;

public readonly record struct InboundComplete(TlsCloseKind CloseKind, int Gen) : ITcpTransportEvent;

public readonly record struct InboundPumpFailed(Exception Error) : ITcpTransportEvent;

public readonly record struct OutboundWriteDone : ITcpTransportEvent;

public readonly record struct OutboundWriteFailed(Exception Error) : ITcpTransportEvent;

public readonly record struct FlushNextCompleted : ITcpTransportEvent;

public interface ITcpTransportEvent;