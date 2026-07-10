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
    private readonly Queue<int> _readyQueue = new();
    private readonly Dictionary<int, PumpSlot<int>> _activeSlots = new();
    private readonly HashSet<int> _windowBlockedStreams = new();
    private readonly List<int> _stillBlockedScratch = new();

    // In-flight reads orphaned by a reconnect (Cleanup): moved OUT of _activeSlots so a replayed
    // request re-registering the SAME stream id cannot overwrite (and leak) them. Each is released
    // when its stale completion finally lands.
    private readonly List<PumpSlot<int>> _draining = new();

    // Bumped on every Cleanup/reconnect; reads are stamped with the generation they start under so
    // a completion from a torn-down connection is identifiable as stale (see HandleReadComplete).
    private int _generation;

    // Owned mutably so a reconnect can cancel the old token and swap in a fresh CTS for the new
    // incarnation's reads.
    private CancellationTokenSource _cts = connectionCts;

    private int _readSlots = 2;
    private int _asyncInFlight;

    public void Register(int streamId, Stream bodyStream, long? contentLength, CancellationToken requestCt)
    {
        var linkedCts = requestCt.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, requestCt)
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
                _stillBlockedScratch.Clear();
                foreach (var blocked in _windowBlockedStreams)
                {
                    if (flowController.GetStreamSendWindow(blocked) >= minRead)
                    {
                        _readyQueue.Enqueue(blocked);
                    }
                    else
                    {
                        _stillBlockedScratch.Add(blocked);
                    }
                }

                _windowBlockedStreams.Clear();
                foreach (var id in _stillBlockedScratch)
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

    public void HandleReadComplete(int streamId, int bytesRead, int generation = 0)
    {
        _asyncInFlight--;

        // Stale completion from a torn-down connection (reconnect bumped the generation). Its slot
        // was moved to _draining at Cleanup; release it there. Do NOT touch _activeSlots — a
        // replayed request may already occupy this reused stream id, and mis-advancing its body (or
        // refunding its window) is exactly the corruption this guard prevents.
        if (generation != _generation)
        {
            ReleaseDrainingOrphan(streamId, generation);
            TryScheduleReads();
            return;
        }

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

            PumpSlotLifecycle.ReleaseSlot(_activeSlots, streamId, slot);
            return;
        }

        ProcessReadResult(slot, bytesRead);
    }

    public void HandleReadFailed(int streamId, Exception reason, int generation = 0)
    {
        _asyncInFlight--;

        if (generation != _generation)
        {
            ReleaseDrainingOrphan(streamId, generation);
            TryScheduleReads();
            return;
        }

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

            PumpSlotLifecycle.ReleaseSlot(_activeSlots, streamId, slot);
            return;
        }

        if (slot.ReservedWindow > 0)
        {
            flowController.Refund(slot.StreamId, slot.ReservedWindow);
            slot.ReservedWindow = 0;
        }

        _activeSlots.Remove(streamId);
        target.OnDrainFailed(streamId, reason);
        slot.DisposeResources();
        slot.Dispose();
    }

    // Releases the orphaned slot that a reconnect moved to _draining, once its outstanding read
    // finally lands. (generation, streamId) uniquely identifies it. The old connection's flow
    // window is dead (the FlowController was reset on reconnect), so the reserved window is NOT
    // refunded — the buffer and linked CTS are simply released so neither leaks.
    private void ReleaseDrainingOrphan(int streamId, int generation)
    {
        for (var i = 0; i < _draining.Count; i++)
        {
            var slot = _draining[i];
            if (slot.Generation == generation && slot.StreamId == streamId)
            {
                _draining.RemoveAt(i);
                slot.CompleteRead();
                slot.ReservedWindow = 0;
                slot.DisposeResources();
                slot.Dispose();
                return;
            }
        }
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

        // Not in flight — clean up immediately, matching MultiplexedBodyPump's discipline (lazy
        // removal from the ready queue is not needed: the slot will simply be missing from
        // _activeSlots when dequeued in TryScheduleReads).
        _windowBlockedStreams.Remove(streamId);
        PumpSlotLifecycle.ReleaseSlot(_activeSlots, streamId, slot);
    }

    public void Cleanup()
    {
        _cts.Cancel();
        _windowBlockedStreams.Clear();
        _readyQueue.Clear();

        // Bump the generation FIRST: every read still in flight was stamped with the old generation,
        // so its late completion is now identifiable as stale (see HandleReadComplete).
        _generation++;

        // A slot with a read still in flight cannot be disposed/returned here: the pending ReadAsync
        // still targets its pooled array, and the returned slot would be re-rented mid-read (buffer
        // UAF + BodyReadComplete mis-route). Move it to _draining (out of _activeSlots so a replayed
        // request reusing this id cannot overwrite and leak it). The CTS cancel above completes the
        // read, and its stale completion releases the draining slot. Non-in-flight slots are released
        // immediately.
        foreach (var (_, slot) in _activeSlots)
        {
            if (slot.IsReadInFlight)
            {
                slot.MarkOrphaned();
                _draining.Add(slot);
            }
            else
            {
                slot.DisposeResources();
                slot.Dispose();
            }
        }

        _activeSlots.Clear();

        // Fresh CTS for the next incarnation's reads; the old (cancelled) one stays referenced by
        // the still-outstanding draining reads until they observe cancellation and complete.
        _cts = new CancellationTokenSource();
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

        slot.Generation = _generation;
        var token = slot.LinkedCts?.Token ?? _cts.Token;
        PumpSlotLifecycle.StartRead(slot, readSize, token, target.StageActor, ref _asyncInFlight);
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
