using System.Buffers;
using System.IO.Pipelines;
using Akka.Actor;
using Servus.Akka.Transport;
using static Servus.Senf;

namespace GaudiHTTP.Protocol;

internal sealed class TransportIo
{
    public delegate (SequencePosition Consumed, SequencePosition Examined) DecodeDataFunc(ReadOnlySequence<byte> data);

    private IConnectionTransport? _transport;
    private int _transportGen;
    private bool _readInProgress;
    private bool _flushInProgress;
    private bool _flushNeeded;
    private int _syncReadBudget;
    private PipeReadState? _readState;
    private PipeFlushState? _flushState;

    private readonly Func<IActorRef> _self;
    private readonly Func<bool> _shouldPause;
    private readonly DecodeDataFunc _decode;
    private readonly Action _onFlushCompleted;
    private readonly Action _onFlushDeferred;
    private readonly Action<Exception?> _onTransportLost;

    internal const int MaxSyncReads = 8;

    public IConnectionTransport? Transport => _transport;
    public bool IsFlushInProgress => _flushInProgress;
    public int TransportGen => _transportGen;

    public TransportIo(
        Func<IActorRef> self,
        Func<bool> shouldPause,
        DecodeDataFunc decode,
        Action onFlushCompleted,
        Action onFlushDeferred,
        Action<Exception?> onTransportLost)
    {
        _self = self;
        _shouldPause = shouldPause;
        _decode = decode;
        _onFlushCompleted = onFlushCompleted;
        _onFlushDeferred = onFlushDeferred;
        _onTransportLost = onTransportLost;
    }

    public void OnConnected(IConnectionTransport? transport)
    {
        _transportGen++;
        _readInProgress = false;
        _flushInProgress = false;
        _flushNeeded = false;
        _syncReadBudget = MaxSyncReads;
        if (transport is not null)
        {
            _transport = transport;
            _readState = new PipeReadState(_transportGen);
            _flushState = new PipeFlushState(_transportGen);
        }
    }

    public void OnDisconnected()
    {
        _transport = null;
        _transportGen++;
        _readInProgress = false;
        _flushInProgress = false;
        _flushNeeded = false;
    }

    public void RequestRead()
    {
        if (_transport is null || _shouldPause() || _readInProgress)
        {
            return;
        }

        _readInProgress = true;

        var vt = _transport.ReadAsync();
        if (vt.IsCompletedSuccessfully && _syncReadBudget > 0)
        {
            _syncReadBudget--;
            _readInProgress = false;
            ProcessReadResult(vt.Result);
            return;
        }

        _syncReadBudget = MaxSyncReads;
        AwaitReadAsync(vt, _transportGen);
    }

    private async void AwaitReadAsync(ValueTask<ReadResult> vt, int gen)
    {
        try
        {
            var result = await vt.ConfigureAwait(false);
            _self().Tell(new ReadCompleted(result, gen), ActorRefs.NoSender);
        }
        catch (Exception ex)
        {
            _self().Tell(new ReadFailed(ex, gen), ActorRefs.NoSender);
        }
    }

    public void RequestFlush()
    {
        if (_transport is null)
        {
            return;
        }

        if (_flushInProgress)
        {
            _flushNeeded = true;
            return;
        }

        var vt = _transport.FlushAsync();
        if (vt.IsCompletedSuccessfully)
        {
            ProcessFlushResult(vt.Result);
        }
        else
        {
            AwaitFlushAsync(vt, _transportGen);
            _flushInProgress = true;
            _onFlushDeferred();
        }
    }

    private async void AwaitFlushAsync(ValueTask<FlushResult> vt, int gen)
    {
        try
        {
            var result = await vt.ConfigureAwait(false);
            _self().Tell(new FlushCompleted(result, gen), ActorRefs.NoSender);
        }
        catch (Exception ex)
        {
            _self().Tell(new FlushFailed(ex, gen), ActorRefs.NoSender);
        }
    }

    public bool OnAsyncResult(object msg)
    {
        switch (msg)
        {
            case ReadCompleted rc:
                if (rc.Gen != _transportGen)
                {
                    Tracing.For("Protocol").Debug(this, "dropping stale ReadCompleted gen={0} current={1}", rc.Gen, _transportGen);
                    return true;
                }
                ProcessReadResult(rc.Result);
                return true;

            case ReadFailed rf:
                if (rf.Gen != _transportGen)
                {
                    Tracing.For("Protocol").Debug(this, "dropping stale ReadFailed gen={0} current={1}", rf.Gen, _transportGen);
                    return true;
                }
                _readInProgress = false;
                Tracing.For("Protocol").Warning(this, "transport read failed: {0}", rf.Ex.Message);
                _onTransportLost(rf.Ex);
                return true;

            case FlushCompleted fc:
                if (fc.Gen != _transportGen)
                {
                    Tracing.For("Protocol").Debug(this, "dropping stale FlushCompleted gen={0} current={1}", fc.Gen, _transportGen);
                    return true;
                }
                ProcessFlushResult(fc.Result);
                return true;

            case FlushFailed ff:
                if (ff.Gen != _transportGen)
                {
                    Tracing.For("Protocol").Debug(this, "dropping stale FlushFailed gen={0} current={1}", ff.Gen, _transportGen);
                    return true;
                }
                _flushInProgress = false;
                Tracing.For("Protocol").Warning(this, "transport flush failed: {0}", ff.Ex.Message);
                _onTransportLost(ff.Ex);
                return true;

            default:
                return false;
        }
    }

    public void Cleanup()
    {
        _transport = null;
        _readInProgress = false;
        _flushInProgress = false;
        _flushNeeded = false;
    }

    private void ProcessReadResult(ReadResult result)
    {
        _readInProgress = false;
        if (result.IsCompleted && result.Buffer.IsEmpty)
        {
            Tracing.For("Protocol").Info(this, "transport closed by remote (empty completed read)");
            _onTransportLost(null);
            return;
        }

        var (consumed, examined) = _decode(result.Buffer);
        _transport!.AdvanceTo(consumed, examined);
        RequestRead();
    }

    private void ProcessFlushResult(FlushResult result)
    {
        _flushInProgress = false;
        if (result.IsCompleted)
        {
            _flushNeeded = false;
            Tracing.For("Protocol").Info(this, "transport closed by remote (flush IsCompleted)");
            _onTransportLost(null);
            return;
        }

        _onFlushCompleted();

        if (_flushNeeded && !_flushInProgress && _transport is not null)
        {
            _flushNeeded = false;
            var vt = _transport.FlushAsync();
            if (vt.IsCompletedSuccessfully)
            {
                ProcessFlushResult(vt.Result);
            }
            else
            {
                AwaitFlushAsync(vt, _transportGen);
                _flushInProgress = true;
            }
        }
    }
}
