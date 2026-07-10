using GaudiHTTP.Pooling;
using static Servus.Senf;

namespace GaudiHTTP.Protocol.Body;

internal sealed class MultiplexedBodyPump(
    IMultiplexedBodyDrainTarget target,
    CancellationTokenSource connectionCts,
    int chunkSize,
    int maxBytesPerStream,
    int maxConcurrentReads = 4)
{
    private readonly Queue<long> _readyQueue = new();
    private readonly Dictionary<long, PumpSlot<long>> _activeSlots = new();
    private readonly List<long> _cleanupScratch = new();

    private int _asyncInFlight;

    // Per-stream outbound byte budget. Each slot carries its own AvailableBytes (seeded to
    // maxBytesPerStream at Register), debited by the actual bytes emitted per DATA frame and
    // credited back per real per-stream transport flush (MultiplexedDataFlushed) via
    // OnCapacityAvailable. Unlike the old connection-wide aggregate counter, a depleted budget
    // parks ONLY the offending stream — sibling streams keep draining — so one slow reader can no
    // longer stall the whole connection, and the shared array pool stays bounded per stream.

    public void Register(long streamId, Stream bodyStream, long? contentLength, CancellationToken requestCt)
    {
        var linkedCts = requestCt.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(connectionCts.Token, requestCt)
            : null;
        var slot = ConnectionObjectPool.Instance.Rent(static () => new PumpSlot<long>());
        slot.Initialize(streamId, bodyStream, requestCt, linkedCts);
        slot.ContentLength = contentLength;
        _activeSlots[streamId] = slot;
        slot.AvailableBytes = maxBytesPerStream;
        slot.IsQueued = true;
        _readyQueue.Enqueue(streamId);
        TryScheduleReads();
    }

    public void HandleReadComplete(long streamId, int bytesRead)
    {
        _asyncInFlight--;

        if (!_activeSlots.TryGetValue(streamId, out var slot))
        {
            return;
        }

        slot.CompleteRead();

        if (slot.IsOrphaned)
        {
            PumpSlotLifecycle.ReleaseSlot(_activeSlots, streamId, slot);
            return;
        }

        ProcessReadResult(slot, bytesRead);
    }

    public void HandleReadFailed(long streamId, Exception reason)
    {
        _asyncInFlight--;

        if (!_activeSlots.TryGetValue(streamId, out var slot))
        {
            return;
        }

        slot.CompleteRead();

        if (slot.IsOrphaned)
        {
            PumpSlotLifecycle.ReleaseSlot(_activeSlots, streamId, slot);
            return;
        }

        _activeSlots.Remove(streamId);
        target.OnDrainFailed(streamId, reason);
        slot.DisposeResources();
        slot.Dispose();
    }

    public void OnCapacityAvailable(long streamId, int bytes)
    {
        if (!_activeSlots.TryGetValue(streamId, out var slot))
        {
            return;
        }

        slot.AvailableBytes = Math.Min(maxBytesPerStream, slot.AvailableBytes + bytes);
        Tracing.For("Protocol").Trace(this, "mux body stream={0} credit={1} budget={2}",
            streamId, bytes, slot.AvailableBytes);

        if (!slot.IsQueued && !slot.IsReadInFlight && slot.AvailableBytes > 0)
        {
            _readyQueue.Enqueue(streamId);
            slot.IsQueued = true;
        }

        TryScheduleReads();
    }

    public void Cancel(long streamId)
    {
        if (!_activeSlots.TryGetValue(streamId, out var slot))
        {
            return;
        }

        slot.LinkedCts?.Cancel();

        if (slot.IsReadInFlight)
        {
            // Async read in flight: mark orphaned, cleanup happens in HandleReadComplete/Failed
            slot.MarkOrphaned();
            return;
        }

        // Not in flight — clean up immediately (lazy removal from ready queue is not needed
        // since the slot will simply be missing from _activeSlots when dequeued)
        PumpSlotLifecycle.ReleaseSlot(_activeSlots, streamId, slot);
    }

    public void Cleanup()
    {
        connectionCts.Cancel();
        _readyQueue.Clear();

        // A slot with a read still in flight cannot be disposed/returned here: the pending ReadAsync
        // still targets its pooled array, and the returned slot would be re-rented mid-read (buffer
        // UAF + BodyReadComplete mis-route). MarkOrphaned and RETAIN it — connectionCts.Cancel above
        // completes the read, and HandleReadComplete/HandleReadFailed releases the orphaned slot then,
        // exactly as Cancel already does per-slot. Only non-in-flight slots are released immediately.
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
        while (_asyncInFlight < maxConcurrentReads && _readyQueue.Count > 0)
        {
            var streamId = _readyQueue.Dequeue();

            if (!_activeSlots.TryGetValue(streamId, out var slot))
            {
                // Slot was cancelled and removed — skip
                continue;
            }

            slot.IsQueued = false;

            if (slot.AvailableBytes <= 0)
            {
                // Parked: this stream has no outbound budget left. It is re-enqueued by
                // OnCapacityAvailable when a real flush credits it — leaving siblings free to drain.
                continue;
            }

            slot.EnsureBuffer(chunkSize);

            StartRead(slot);
        }
    }

    private void StartRead(PumpSlot<long> slot)
    {
        var token = slot.LinkedCts?.Token ?? connectionCts.Token;
        PumpSlotLifecycle.StartRead(slot, chunkSize, token, target.StageActor, ref _asyncInFlight);
    }

    private void ProcessReadResult(PumpSlot<long> slot, int bytesRead)
    {
        if (bytesRead == 0)
        {
            target.EmitDataFrames(slot.StreamId, default, endStream: true);
            CompleteDrain(slot);
            return;
        }

        slot.AvailableBytes -= bytesRead;
        target.EmitDataFrames(slot.StreamId, slot.Buffer!.Memory[..bytesRead], endStream: false);
        Tracing.For("Protocol").Trace(this, "mux body stream={0} debit={1} budget={2}",
            slot.StreamId, bytesRead, slot.AvailableBytes);

        if (slot.AvailableBytes > 0)
        {
            _readyQueue.Enqueue(slot.StreamId);
            slot.IsQueued = true;
        }

        TryScheduleReads();
    }

    private void CompleteDrain(PumpSlot<long> slot)
    {
        _activeSlots.Remove(slot.StreamId);
        target.OnDrainComplete(slot.StreamId);
        slot.DisposeResources();
        slot.Dispose();
    }

}
