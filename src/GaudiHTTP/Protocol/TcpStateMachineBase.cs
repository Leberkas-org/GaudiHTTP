using System.Buffers;
using Akka.Actor;
using Servus.Akka.Transport;

namespace GaudiHTTP.Protocol;

internal abstract class TcpStateMachineBase<TOps>
{
    private readonly TransportIo _tio;

    protected TOps Ops { get; }
    protected IConnectionTransport? Transport => _tio.Transport;
    protected int TransportGen => _tio.TransportGen;
    protected bool IsFlushInProgress => _tio.IsFlushInProgress;

    protected abstract IActorRef Self { get; }
    protected abstract bool ShouldPauseReads { get; }

    protected abstract (SequencePosition Consumed, SequencePosition Examined) DecodeData(
        ReadOnlySequence<byte> data);

    protected abstract void OnTransportConnected(ConnectionInfo info);
    protected abstract void OnTransportDisconnected(DisconnectReason reason);
    protected abstract void OnFlushCompleted();
    protected abstract void OnFlushDeferred();
    protected abstract void OnTransportLost(Exception? ex);

    protected TcpStateMachineBase(TOps ops)
    {
        Ops = ops;
        _tio = new TransportIo(
            self: () => Self,
            shouldPause: () => ShouldPauseReads,
            decode: DecodeData,
            onFlushCompleted: () => OnFlushCompleted(),
            onFlushDeferred: () => OnFlushDeferred(),
            onTransportLost: ex => OnTransportLost(ex));
    }

    public bool DispatchLifecycleEvent(ITransportInbound data)
    {
        switch (data)
        {
            case TransportConnected tc:
                _tio.OnConnected(tc.Transport);
                OnTransportConnected(tc.Info);
                _tio.RequestRead();
                return true;
            case TransportDisconnected td:
                _tio.OnDisconnected();
                OnTransportDisconnected(td.Reason);
                return true;
            default:
                return false;
        }
    }

    public bool TryHandleAsyncResult(object msg) => _tio.OnAsyncResult(msg);

    protected void RequestFlush() => _tio.RequestFlush();

    protected void RequestRead() => _tio.RequestRead();

    protected void CleanupTransportIo() => _tio.Cleanup();
}
