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

    // In-flight reads orphaned by a reconnect (Cleanup): moved OUT of _activeSlots so a replayed
    // request re-registering the SAME stream id cannot overwrite (and leak) them. Each is released
    // when its stale completion finally lands (the old connection's CTS cancellation forces it).
    private readonly List<PumpSlot<long>> _draining = new();

    // Bumped on every Cleanup/reconnect. Reads are stamped with the generation they start under;
    // a completion carrying an older generation is stale (see HandleReadComplete).
    private int _generation;

    // Owned mutably so a reconnect can cancel the old token (forcing outstanding reads to complete
    // and release their draining slots) and swap in a fresh CTS for the new incarnation's reads.
    private CancellationTokenSource _cts = connectionCts;

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
            ? CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, requestCt)
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

    public void HandleReadComplete(long streamId, int bytesRead, int generation = 0)
    {
        _asyncInFlight--;

        // Stale completion from a torn-down connection (reconnect bumped the generation). Its slot
        // was moved to _draining at Cleanup; release it there. Do NOT touch _activeSlots — a
        // replayed request may already occupy this reused stream id, and mis-advancing its body is
        // exactly the corruption this guard prevents.
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
            PumpSlotLifecycle.ReleaseSlot(_activeSlots, streamId, slot);
            return;
        }

        ProcessReadResult(slot, bytesRead);
    }

    public void HandleReadFailed(long streamId, Exception reason, int generation = 0)
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
            PumpSlotLifecycle.ReleaseSlot(_activeSlots, streamId, slot);
            return;
        }

        _activeSlots.Remove(streamId);
        target.OnDrainFailed(streamId, reason);
        slot.DisposeResources();
        slot.Dispose();
    }

    // Releases the orphaned slot that a reconnect moved to _draining, once its outstanding read
    // finally lands. (generation, streamId) uniquely identifies it: within one incarnation a stream
    // id is registered at most once, and each reconnect bumps the generation. The old connection's
    // flow/budget state is dead, so nothing is refunded — the buffer and linked CTS are simply
    // released so neither leaks.
    private void ReleaseDrainingOrphan(long streamId, int generation)
    {
        for (var i = 0; i < _draining.Count; i++)
        {
            var slot = _draining[i];
            if (slot.Generation == generation && slot.StreamId == streamId)
            {
                _draining.RemoveAt(i);
                slot.CompleteRead();
                slot.DisposeResources();
                slot.Dispose();
                return;
            }
        }
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
        _cts.Cancel();
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
        slot.Generation = _generation;
        var token = slot.LinkedCts?.Token ?? _cts.Token;
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
