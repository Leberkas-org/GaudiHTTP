using Akka.Actor;
using TurboHTTP.Pooling;

namespace TurboHTTP.Protocol.Body;

internal sealed class MultiplexedBodyPump
{
    private readonly IBodyDrainTarget<long> _target;
    private readonly ConnectionPoolContext _poolContext;
    private readonly CancellationTokenSource _connectionCts;
    private readonly int _chunkSize;
    private readonly int _maxConcurrentReads;

    private readonly Queue<long> _readyQueue = new();
    private readonly Dictionary<long, BodyDrainSlot<long>> _activeSlots = new();

    private int _asyncInFlight;

    public MultiplexedBodyPump(
        IBodyDrainTarget<long> target,
        ConnectionPoolContext poolContext,
        CancellationTokenSource connectionCts,
        int chunkSize,
        int maxConcurrentReads = 4)
    {
        _target = target;
        _poolContext = poolContext;
        _connectionCts = connectionCts;
        _chunkSize = chunkSize;
        _maxConcurrentReads = maxConcurrentReads;
    }

    public void Register(long streamId, Stream bodyStream, long? contentLength, CancellationToken requestCt)
    {
        var slot = _poolContext.Rent(static () => new BodyDrainSlot<long>());
        CancellationTokenSource? linkedCts = requestCt.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(_connectionCts.Token, requestCt)
            : null;
        var effectiveToken = linkedCts?.Token ?? _connectionCts.Token;
        slot.Initialize(streamId, bodyStream, contentLength, effectiveToken, linkedCts);
        slot.EnsureBuffer(_chunkSize);
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

        slot.CompleteAsyncRead();

        if (slot.IsOrphaned)
        {
            RemoveAndReturnSlot(streamId, slot);
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

        slot.CompleteAsyncRead();

        if (slot.IsOrphaned)
        {
            RemoveAndReturnSlot(streamId, slot);
            return;
        }

        _activeSlots.Remove(streamId);
        _target.OnDrainFailed(streamId, reason);
        slot.DisposeResources();
        _poolContext.Return(slot);
    }

    public void HandleDrainContinue(long streamId)
    {
        if (!_activeSlots.TryGetValue(streamId, out var slot))
        {
            return;
        }

        slot.CompleteSyncRead();

        if (slot.IsOrphaned)
        {
            RemoveAndReturnSlot(streamId, slot);
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
            slot.MarkOrphaned();
            return;
        }

        RemoveAndReturnSlot(streamId, slot);
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
        _readyQueue.Clear();
    }

    private void TryScheduleReads()
    {
        while (_asyncInFlight < _maxConcurrentReads && _readyQueue.Count > 0)
        {
            var streamId = _readyQueue.Dequeue();

            if (!_activeSlots.TryGetValue(streamId, out var slot))
            {
                continue;
            }

            var result = BodyPumpHelper.StartRead(slot, _chunkSize, _target.PipeToTarget);

            switch (result.Outcome)
            {
                case BodyPumpHelper.ReadOutcome.CompletedSynchronously:
                    ProcessReadResult(slot, result.BytesRead);
                    break;

                case BodyPumpHelper.ReadOutcome.Dispatched:
                    _asyncInFlight++;
                    break;
            }
        }
    }

    private void ProcessReadResult(BodyDrainSlot<long> slot, int bytesRead)
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

    private void CompleteDrain(BodyDrainSlot<long> slot)
    {
        _activeSlots.Remove(slot.StreamId);
        _target.OnDrainComplete(slot.StreamId);
        slot.DisposeResources();
        _poolContext.Return(slot);
    }

    private void RemoveAndReturnSlot(long streamId, BodyDrainSlot<long> slot)
    {
        _activeSlots.Remove(streamId);
        slot.DisposeResources();
        _poolContext.Return(slot);
    }
}
