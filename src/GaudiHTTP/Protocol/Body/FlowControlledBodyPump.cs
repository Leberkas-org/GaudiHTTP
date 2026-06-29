using System.Buffers;
using Akka.Actor;
using GaudiHTTP.Pooling;
using GaudiHTTP.Protocol.Syntax.Http2;

namespace GaudiHTTP.Protocol.Body;

internal sealed class FlowControlledBodyPump
{
    private const int MaxSyncReadsPerDispatch = 64;

    private readonly IBodyDrainTarget _target;
    private readonly FlowController _flowController;
    private readonly CancellationTokenSource _connectionCts;
    private readonly ConnectionObjectPool _poolContext;
    private readonly int _chunkSize;
    private readonly int _hardCap;

    private readonly Queue<int> _readyQueue = new();
    private readonly Dictionary<int, PumpSlot<int>> _activeSlots = new();
    private readonly HashSet<int> _cancelledStreams = new();
    private readonly HashSet<int> _windowBlockedStreams = new();

    private int _readSlots = 2;
    private int _asyncInFlight;

    public FlowControlledBodyPump(
        IBodyDrainTarget target,
        FlowController flowController,
        CancellationTokenSource connectionCts,
        ConnectionObjectPool poolContext,
        int chunkSize,
        int hardCap)
    {
        _target = target;
        _flowController = flowController;
        _connectionCts = connectionCts;
        _poolContext = poolContext;
        _chunkSize = chunkSize;
        _hardCap = hardCap;
    }

    public void Register(int streamId, Stream bodyStream, long? contentLength, CancellationToken requestCt)
    {
        var linkedCts = requestCt.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(_connectionCts.Token, requestCt)
            : null;
        var slot = _poolContext.Rent(static () => new PumpSlot<int>());
        slot.Initialize(streamId, bodyStream, requestCt, linkedCts);
        slot.ContentLength = contentLength;
        _activeSlots[streamId] = slot;

        if (_flowController.GetStreamSendWindow(streamId) > 0 && _flowController.ConnectionSendWindow > 0)
        {
            _readyQueue.Enqueue(streamId);
            TryScheduleReads();
        }
        else
        {
            _windowBlockedStreams.Add(streamId);
        }
    }

    public void OnWindowUpdate(int streamId)
    {
        var minRead = _chunkSize / 2;

        if (streamId == 0)
        {
            if (_flowController.ConnectionSendWindow >= minRead)
            {
                var stillBlocked = new List<int>();
                foreach (var blocked in _windowBlockedStreams)
                {
                    if (_flowController.GetStreamSendWindow(blocked) >= minRead)
                    {
                        _readyQueue.Enqueue(blocked);
                    }
                    else
                    {
                        stillBlocked.Add(blocked);
                    }
                }

                _windowBlockedStreams.Clear();
                foreach (var id in stillBlocked)
                {
                    _windowBlockedStreams.Add(id);
                }
            }
        }
        else if (_windowBlockedStreams.Remove(streamId))
        {
            _readyQueue.Enqueue(streamId);
        }

        TryScheduleReads();
    }

    public void HandleReadComplete(int streamId, int bytesRead)
    {
        _asyncInFlight--;

        if (!_activeSlots.TryGetValue(streamId, out var slot))
        {
            return;
        }

        slot.CompleteRead();

        if (slot.IsOrphaned)
        {
            if (slot.ReservedWindow > 0)
            {
                _flowController.Refund(slot.StreamId, slot.ReservedWindow);
                slot.ReservedWindow = 0;
            }

            slot.DisposeResources();
            _poolContext.Return(slot);
            _activeSlots.Remove(streamId);
            return;
        }

        ProcessReadResult(slot, bytesRead);
    }

    public void HandleReadFailed(int streamId, Exception reason)
    {
        _asyncInFlight--;

        if (!_activeSlots.TryGetValue(streamId, out var slot))
        {
            return;
        }

        slot.CompleteRead();

        if (slot.IsOrphaned)
        {
            if (slot.ReservedWindow > 0)
            {
                _flowController.Refund(slot.StreamId, slot.ReservedWindow);
                slot.ReservedWindow = 0;
            }

            slot.DisposeResources();
            _poolContext.Return(slot);
            _activeSlots.Remove(streamId);
            return;
        }

        _activeSlots.Remove(streamId);
        _target.OnDrainFailed(streamId, reason);
        slot.DisposeResources();
        _poolContext.Return(slot);
    }

    public void HandleBodyReadContinue(int streamId)
    {
        if (!_activeSlots.TryGetValue(streamId, out var slot))
        {
            return;
        }

        slot.ResetSyncReads();

        if (slot.IsOrphaned)
        {
            slot.DisposeResources();
            _poolContext.Return(slot);
            _activeSlots.Remove(streamId);
            return;
        }

        _readyQueue.Enqueue(streamId);
        TryScheduleReads();
    }

    public void Cancel(int streamId)
    {
        if (!_activeSlots.TryGetValue(streamId, out var slot))
        {
            return;
        }

        slot.LinkedCts?.Cancel();

        if (slot.IsReadInFlight)
        {
            slot.MarkOrphaned();
            return;
        }

        if (_windowBlockedStreams.Remove(streamId))
        {
            _activeSlots.Remove(streamId);
            slot.DisposeResources();
            _poolContext.Return(slot);
            return;
        }

        _cancelledStreams.Add(streamId);
    }

    public void Cleanup()
    {
        _connectionCts.Cancel();

        foreach (var (_, slot) in _activeSlots)
        {
            if (!slot.IsOrphaned)
            {
                slot.DisposeResources();
                _poolContext.Return(slot);
            }
        }

        _activeSlots.Clear();
        _windowBlockedStreams.Clear();
        _cancelledStreams.Clear();
        _readyQueue.Clear();
    }

    private void TryScheduleReads()
    {
        var connWindow = _flowController.ConnectionSendWindow;

        if (connWindow <= 0)
        {
            return;
        }

        while (_asyncInFlight < Math.Min(_readSlots, _hardCap) && _readyQueue.Count > 0)
        {
            var streamId = _readyQueue.Dequeue();

            if (_cancelledStreams.Remove(streamId))
            {
                if (_activeSlots.TryGetValue(streamId, out var cancelled))
                {
                    _activeSlots.Remove(streamId);
                    cancelled.DisposeResources();
                    _poolContext.Return(cancelled);
                }

                continue;
            }

            if (!_activeSlots.TryGetValue(streamId, out var slot))
            {
                continue;
            }

            var streamWindow = _flowController.GetStreamSendWindow(streamId);
            connWindow = _flowController.ConnectionSendWindow;
            var available = (int)Math.Min(streamWindow, connWindow);
            var minReadSize = _chunkSize / 2;

            if (available < minReadSize)
            {
                _windowBlockedStreams.Add(streamId);
                continue;
            }

            if (slot.ConsecutiveSyncReads >= MaxSyncReadsPerDispatch)
            {
                slot.ResetSyncReads();
                slot.BeginRead();  // marks as in-flight for yield
                _target.StageActor.Tell(new BodyReadContinue<int>(slot.StreamId), ActorRefs.NoSender);
                continue;
            }

            slot.EnsureBuffer(_chunkSize);

            StartRead(slot);
        }
    }

    private void StartRead(PumpSlot<int> slot)
    {
        var streamWindow = _flowController.GetStreamSendWindow(slot.StreamId);
        var connWindow = _flowController.ConnectionSendWindow;
        var readSize = (int)Math.Min(Math.Min((long)_chunkSize, streamWindow), connWindow);

        _flowController.Reserve(slot.StreamId, readSize);
        slot.ReservedWindow = readSize;

        var token = slot.LinkedCts?.Token ?? _connectionCts.Token;
        slot.BeginRead();
        var vt = slot.BodyStream!.ReadAsync(slot.Buffer!.Memory[..readSize], token);

        if (vt.IsCompletedSuccessfully)
        {
            slot.CompleteRead();
            _target.StageActor.Tell(
                slot.CachedSuccessTransform!(vt.Result),
                ActorRefs.NoSender);
            return;
        }

        slot.ResetSyncReads();
        _asyncInFlight++;
        vt.PipeTo(
            _target.StageActor,
            success: slot.CachedSuccessTransform,
            failure: slot.CachedFailureTransform);
    }

    private void ProcessReadResult(PumpSlot<int> slot, int bytesRead)
    {
        var refund = slot.ReservedWindow - bytesRead;
        if (refund > 0)
        {
            _flowController.Refund(slot.StreamId, refund);
        }

        slot.ReservedWindow = 0;

        if (bytesRead == 0)
        {
            _target.EmitDataFrames(slot.StreamId, default, endStream: true);
            CompleteDrain(slot);
            return;
        }

        _target.EmitDataFrames(slot.StreamId, slot.Buffer!.Memory[..bytesRead], endStream: false);
        _readSlots = Math.Min(_readSlots + 1, _hardCap);
        _readyQueue.Enqueue(slot.StreamId);
        TryScheduleReads();
    }

    private void CompleteDrain(PumpSlot<int> slot)
    {
        _activeSlots.Remove(slot.StreamId);
        _target.OnDrainComplete(slot.StreamId);
        slot.DisposeResources();
        _poolContext.Return(slot);
    }

}
