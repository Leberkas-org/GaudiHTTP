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
    private readonly List<int> _cleanupScratch = new();

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

            PumpSlotLifecycle.ReleaseSlot(_activeSlots, streamId, slot);
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
        connectionCts.Cancel();
        _windowBlockedStreams.Clear();
        _readyQueue.Clear();

        // A slot with a read still in flight cannot be disposed/returned here: the pending ReadAsync
        // still targets its pooled array, and the returned slot would be re-rented mid-read (buffer
        // UAF + BodyReadComplete mis-route). MarkOrphaned and RETAIN it — connectionCts.Cancel above
        // completes the read, and HandleReadComplete/HandleReadFailed releases the orphaned slot then
        // (refunding its reserved window), exactly as Cancel already does per-slot. Only non-in-flight
        // slots are released immediately.
        _cleanupScratch.Clear();
        foreach (var (streamId, slot) in _activeSlots)
        {
            if (slot.IsReadInFlight)
            {
                slot.MarkOrphaned();
            }
            else
            {
                slot.DisposeResources();
                slot.Dispose();
                _cleanupScratch.Add(streamId);
            }
        }

        foreach (var streamId in _cleanupScratch)
        {
            _activeSlots.Remove(streamId);
        }

        _cleanupScratch.Clear();
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

        var token = slot.LinkedCts?.Token ?? connectionCts.Token;
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
