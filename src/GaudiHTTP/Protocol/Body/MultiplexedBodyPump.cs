using Akka.Actor;
using GaudiHTTP.Pooling;

namespace GaudiHTTP.Protocol.Body;

internal sealed class MultiplexedBodyPump(
    IMultiplexedBodyDrainTarget target,
    CancellationTokenSource connectionCts,
    int chunkSize,
    int maxCapacity,
    int maxConcurrentReads = 4)
{
    private const int MaxSyncReadsPerDispatch = 64;

    private readonly Queue<long> _readyQueue = new();
    private readonly Dictionary<long, PumpSlot<long>> _activeSlots = new();

    private int _asyncInFlight;

    // Connection-level outbound credit shared across all multiplexed streams. Decremented once per
    // emitted DATA frame and replenished by OnCapacityAvailable when the transport drains an
    // outbound item. Without this gate the pump reads an entire in-memory body as fast as the
    // mailbox cycles, flooding the per-stream output pipes and exhausting the shared array pool.
    private readonly int _maxCapacity = maxCapacity;
    private int _availableCapacity = maxCapacity;

    public void Register(long streamId, Stream bodyStream, long? contentLength, CancellationToken requestCt)
    {
        var linkedCts = requestCt.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(connectionCts.Token, requestCt)
            : null;
        var slot = ConnectionObjectPool.Instance.Rent(static () => new PumpSlot<long>());
        slot.Initialize(streamId, bodyStream, requestCt, linkedCts);
        slot.ContentLength = contentLength;
        _activeSlots[streamId] = slot;
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
            slot.DisposeResources();
            slot.Dispose();
            _activeSlots.Remove(streamId);
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

    public void OnCapacityAvailable()
    {
        if (_availableCapacity < _maxCapacity)
        {
            _availableCapacity++;
        }

        TryScheduleReads();
    }

    public void HandleBodyReadContinue(long streamId)
    {
        if (!_activeSlots.TryGetValue(streamId, out var slot))
        {
            return;
        }

        // Clear counter and in-flight marker set by starvation guard
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
        _activeSlots.Remove(streamId);
        slot.DisposeResources();
        slot.Dispose();
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
        _readyQueue.Clear();
    }

    private void TryScheduleReads()
    {
        while (_asyncInFlight < maxConcurrentReads && _availableCapacity > 0 && _readyQueue.Count > 0)
        {
            var streamId = _readyQueue.Dequeue();

            if (!_activeSlots.TryGetValue(streamId, out var slot))
            {
                // Slot was cancelled and removed — skip
                continue;
            }

            // Starvation guard: yield after MaxSyncReadsPerDispatch consecutive sync reads so
            // the actor thread can process other messages between bursts. This yield does not emit
            // a frame, so it must not consume outbound credit.
            if (slot.ConsecutiveSyncReads >= MaxSyncReadsPerDispatch)
            {
                slot.ResetSyncReads();
                // Use BeginRead as a yield-in-progress marker so re-entrant calls from
                // ProcessReadResult cannot start another read while waiting for HandleBodyReadContinue.
                slot.BeginRead();
                target.StageActor.Tell(new BodyReadContinue<long>(slot.StreamId), ActorRefs.NoSender);
                continue;
            }

            _availableCapacity--;
            slot.EnsureBuffer(chunkSize);

            StartRead(slot);
        }
    }

    private void StartRead(PumpSlot<long> slot)
    {
        var token = slot.LinkedCts?.Token ?? connectionCts.Token;
        slot.BeginRead();
        var vt = slot.BodyStream!.ReadAsync(slot.Buffer!.Memory[..chunkSize], token);

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

    private void ProcessReadResult(PumpSlot<long> slot, int bytesRead)
    {
        if (bytesRead == 0)
        {
            target.EmitDataFrames(slot.StreamId, default, endStream: true);
            CompleteDrain(slot);
            return;
        }

        target.EmitDataFrames(slot.StreamId, slot.Buffer!.Memory[..bytesRead], endStream: false);
        _readyQueue.Enqueue(slot.StreamId);
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
