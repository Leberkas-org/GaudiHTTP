using Akka.Actor;
using GaudiHTTP.Pooling;
using GaudiHTTP.Protocol.Syntax.Http2;

namespace GaudiHTTP.Protocol.Body;

internal sealed class FlowControlledBodyPump(
    IBodyDrainTarget target,
    FlowController flowController,
    CancellationTokenSource connectionCts,
    int chunkSize,
    int hardCap)
{
    private const int MaxSyncReadsPerDispatch = 64;

    private readonly Queue<int> _readyQueue = new();
    private readonly Dictionary<int, PumpSlot<int>> _activeSlots = new();
    private readonly HashSet<int> _cancelledStreams = new();
    private readonly HashSet<int> _windowBlockedStreams = new();

    private int _readSlots = 2;
    private int _asyncInFlight;

    public void Register(int streamId, Stream bodyStream, long? contentLength, CancellationToken requestCt)
    {
        var linkedCts = requestCt.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(connectionCts.Token, requestCt)
            : null;
        var slot = ConnectionObjectPool.Instance.Rent(static () => new PumpSlot<int>());
        slot.Initialize(streamId, bodyStream, requestCt, linkedCts);
        slot.ContentLength = contentLength;
        _activeSlots[streamId] = slot;

        if (flowController.GetStreamSendWindow(streamId) > 0 && flowController.ConnectionSendWindow > 0)
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
        var minRead = chunkSize / 2;

        if (streamId == 0)
        {
            if (flowController.ConnectionSendWindow >= minRead)
            {
                var stillBlocked = new List<int>();
                foreach (var blocked in _windowBlockedStreams)
                {
                    if (flowController.GetStreamSendWindow(blocked) >= minRead)
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
                flowController.Refund(slot.StreamId, slot.ReservedWindow);
                slot.ReservedWindow = 0;
            }

            slot.DisposeResources();
            slot.Dispose();
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
                flowController.Refund(slot.StreamId, slot.ReservedWindow);
                slot.ReservedWindow = 0;
            }

            slot.DisposeResources();
            slot.Dispose();
            _activeSlots.Remove(streamId);
            return;
        }

        _activeSlots.Remove(streamId);
        target.OnDrainFailed(streamId, reason);
        slot.DisposeResources();
        slot.Dispose();
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
            slot.Dispose();
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
            slot.Dispose();
            return;
        }

        _cancelledStreams.Add(streamId);
    }

    public void Cleanup()
    {
        connectionCts.Cancel();

        foreach (var (_, slot) in _activeSlots)
        {
            if (!slot.IsOrphaned)
            {
                slot.DisposeResources();
                slot.Dispose();
            }
        }

        _activeSlots.Clear();
        _windowBlockedStreams.Clear();
        _cancelledStreams.Clear();
        _readyQueue.Clear();
    }

    private void TryScheduleReads()
    {
        var connWindow = flowController.ConnectionSendWindow;

        if (connWindow <= 0)
        {
            return;
        }

        while (_asyncInFlight < Math.Min(_readSlots, hardCap) && _readyQueue.Count > 0)
        {
            var streamId = _readyQueue.Dequeue();

            if (_cancelledStreams.Remove(streamId))
            {
                if (_activeSlots.TryGetValue(streamId, out var cancelled))
                {
                    _activeSlots.Remove(streamId);
                    cancelled.DisposeResources();
                    cancelled.Dispose();
                }

                continue;
            }

            if (!_activeSlots.TryGetValue(streamId, out var slot))
            {
                continue;
            }

            var streamWindow = flowController.GetStreamSendWindow(streamId);
            connWindow = flowController.ConnectionSendWindow;
            var available = (int)Math.Min(streamWindow, connWindow);
            var minReadSize = chunkSize / 2;

            if (available < minReadSize)
            {
                _windowBlockedStreams.Add(streamId);
                continue;
            }

            if (slot.ConsecutiveSyncReads >= MaxSyncReadsPerDispatch)
            {
                slot.ResetSyncReads();
                slot.BeginRead();  // marks as in-flight for yield
                target.StageActor.Tell(new BodyReadContinue<int>(slot.StreamId), ActorRefs.NoSender);
                continue;
            }

            slot.EnsureBuffer(chunkSize);

            StartRead(slot);
        }
    }

    private void StartRead(PumpSlot<int> slot)
    {
        var streamWindow = flowController.GetStreamSendWindow(slot.StreamId);
        var connWindow = flowController.ConnectionSendWindow;
        var readSize = (int)Math.Min(Math.Min((long)chunkSize, streamWindow), connWindow);

        flowController.Reserve(slot.StreamId, readSize);
        slot.ReservedWindow = readSize;

        var token = slot.LinkedCts?.Token ?? connectionCts.Token;
        slot.BeginRead();
        var vt = slot.BodyStream!.ReadAsync(slot.Buffer!.Memory[..readSize], token);

        if (vt.IsCompletedSuccessfully)
        {
            // Force-async: identical to the PipeTo path below but delivering the already-known
            // result. The slot stays IsReadInFlight (from BeginRead) and counted in _asyncInFlight
            // across the mailbox hop so HandleReadComplete's CompleteRead/_asyncInFlight-- balance
            // and no re-entrant schedule can touch slot.Buffer before the completion emits it.
            slot.ResetSyncReads();
            _asyncInFlight++;
            target.StageActor.Tell(
                slot.CachedSuccessTransform!(vt.Result),
                ActorRefs.NoSender);
            return;
        }

        slot.ResetSyncReads();
        _asyncInFlight++;
        vt.PipeTo(
            target.StageActor,
            success: slot.CachedSuccessTransform,
            failure: slot.CachedFailureTransform);
    }

    private void ProcessReadResult(PumpSlot<int> slot, int bytesRead)
    {
        var refund = slot.ReservedWindow - bytesRead;
        if (refund > 0)
        {
            flowController.Refund(slot.StreamId, refund);
        }

        slot.ReservedWindow = 0;

        if (bytesRead == 0)
        {
            target.EmitDataFrames(slot.StreamId, default, endStream: true);
            CompleteDrain(slot);
            return;
        }

        target.EmitDataFrames(slot.StreamId, slot.Buffer!.Memory[..bytesRead], endStream: false);
        _readSlots = Math.Min(_readSlots + 1, hardCap);
        _readyQueue.Enqueue(slot.StreamId);
        TryScheduleReads();
    }

    private void CompleteDrain(PumpSlot<int> slot)
    {
        _activeSlots.Remove(slot.StreamId);
        target.OnDrainComplete(slot.StreamId);
        slot.DisposeResources();
        slot.Dispose();
    }

}
