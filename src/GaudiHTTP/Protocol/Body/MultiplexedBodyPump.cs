using System.Buffers;
using Akka.Actor;

namespace GaudiHTTP.Protocol.Body;

internal sealed class MultiplexedBodyPump
{
    private const int MaxSyncReadsPerDispatch = 64;

    private readonly IMultiplexedBodyDrainTarget _target;
    private readonly CancellationTokenSource _connectionCts;
    private readonly int _chunkSize;
    private readonly int _maxConcurrentReads;
    private readonly int _maxPoolSize;

    private readonly Queue<long> _readyQueue = new();
    private readonly Dictionary<long, PumpSlot> _activeSlots = new();
    private readonly Stack<PumpSlot> _slotPool = new();

    private int _asyncInFlight;

    public MultiplexedBodyPump(
        IMultiplexedBodyDrainTarget target,
        CancellationTokenSource connectionCts,
        int chunkSize,
        int maxConcurrentReads = 4)
    {
        _target = target;
        _connectionCts = connectionCts;
        _chunkSize = chunkSize;
        _maxConcurrentReads = maxConcurrentReads;
        _maxPoolSize = maxConcurrentReads * 2;
    }

    public void Register(long streamId, Stream bodyStream, long? contentLength, CancellationToken requestCt)
    {
        var slot = RentSlot();
        slot.StreamId = streamId;
        slot.BodyStream = bodyStream;
        slot.ContentLength = contentLength;
        slot.RequestCt = requestCt;
        slot.LinkedCts = requestCt.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(_connectionCts.Token, requestCt)
            : null;
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

        slot.IsReadInFlight = false;
        slot.ConsecutiveSyncReads = 0;

        if (slot.IsOrphaned)
        {
            DisposeSlotResources(slot);
            ReturnSlot(slot);
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

        slot.IsReadInFlight = false;
        slot.ConsecutiveSyncReads = 0;

        if (slot.IsOrphaned)
        {
            DisposeSlotResources(slot);
            ReturnSlot(slot);
            _activeSlots.Remove(streamId);
            return;
        }

        _activeSlots.Remove(streamId);
        _target.OnDrainFailed(streamId, reason);
        DisposeSlotResources(slot);
        ReturnSlot(slot);
    }

    public void HandleDrainContinue(long streamId)
    {
        if (!_activeSlots.TryGetValue(streamId, out var slot))
        {
            return;
        }

        // Clear in-flight marker set by starvation guard
        slot.IsReadInFlight = false;

        if (slot.IsOrphaned)
        {
            DisposeSlotResources(slot);
            ReturnSlot(slot);
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
            slot.IsOrphaned = true;
            return;
        }

        // Not in flight — clean up immediately (lazy removal from ready queue is not needed
        // since the slot will simply be missing from _activeSlots when dequeued)
        _activeSlots.Remove(streamId);
        DisposeSlotResources(slot);
        ReturnSlot(slot);
    }

    public void Cleanup()
    {
        _connectionCts.Cancel();

        foreach (var (_, slot) in _activeSlots)
        {
            if (!slot.IsOrphaned)
            {
                DisposeSlotResources(slot);
                ReturnSlot(slot);
            }
        }

        _activeSlots.Clear();
        _readyQueue.Clear();
    }

    private void TryScheduleReads()
    {
        while (_asyncInFlight < _maxConcurrentReads && _readyQueue.Count > 0)
        {
            var streamId = _readyQueue.Dequeue();

            if (!_activeSlots.TryGetValue(streamId, out var slot))
            {
                // Slot was cancelled and removed — skip
                continue;
            }

            // Starvation guard: yield after MaxSyncReadsPerDispatch consecutive sync reads so
            // the actor thread can process other messages between bursts.
            if (slot.ConsecutiveSyncReads >= MaxSyncReadsPerDispatch)
            {
                slot.ConsecutiveSyncReads = 0;
                // Use IsReadInFlight as a yield-in-progress marker so re-entrant calls from
                // ProcessReadResult cannot start another read while waiting for HandleDrainContinue.
                slot.IsReadInFlight = true;
                _target.StageActor.Tell(new MultiplexedDrainContinue(slot.StreamId), ActorRefs.NoSender);
                continue;
            }

            if (slot.Buffer is null)
            {
                slot.Buffer = MemoryPool<byte>.Shared.Rent(Math.Max(_chunkSize, 256));
            }

            StartRead(slot);
        }
    }

    private void StartRead(PumpSlot slot)
    {
        var token = slot.LinkedCts?.Token ?? _connectionCts.Token;
        slot.IsReadInFlight = true;
        var vt = slot.BodyStream!.ReadAsync(slot.Buffer!.Memory[.._chunkSize], token);

        if (vt.IsCompletedSuccessfully)
        {
            slot.IsReadInFlight = false;
            slot.ConsecutiveSyncReads++;
            ProcessReadResult(slot, vt.Result);
            return;
        }

        slot.ConsecutiveSyncReads = 0;
        _asyncInFlight++;
        var streamId = slot.StreamId;
        vt.PipeTo(
            _target.StageActor,
            success: bytesRead => new MultiplexedDrainReadComplete(streamId, bytesRead),
            failure: ex => new MultiplexedDrainReadFailed(streamId, ex));
    }

    private void ProcessReadResult(PumpSlot slot, int bytesRead)
    {
        if (bytesRead == 0)
        {
            _target.EmitDataFrames(slot.StreamId, default, endStream: true);
            CompleteDrain(slot);
            return;
        }

        _target.EmitDataFrames(slot.StreamId, slot.Buffer!.Memory[..bytesRead], endStream: false);
        _readyQueue.Enqueue(slot.StreamId);
        TryScheduleReads();
    }

    private void CompleteDrain(PumpSlot slot)
    {
        _activeSlots.Remove(slot.StreamId);
        _target.OnDrainComplete(slot.StreamId);
        DisposeSlotResources(slot);
        ReturnSlot(slot);
    }

    private static void DisposeSlotResources(PumpSlot slot)
    {
        slot.Buffer?.Dispose();
        slot.LinkedCts?.Dispose();
    }

    private PumpSlot RentSlot()
    {
        return _slotPool.TryPop(out var slot) ? slot : new PumpSlot();
    }

    private void ReturnSlot(PumpSlot slot)
    {
        slot.Reset();

        if (_slotPool.Count < _maxPoolSize)
        {
            _slotPool.Push(slot);
        }
    }
}
