using Akka.Actor;
using TurboHTTP.Pooling;
using TurboHTTP.Protocol.Syntax.Http2;

namespace TurboHTTP.Protocol.Body;

internal sealed class FlowControlledBodyPump
{
    private readonly IBodyDrainTarget<int> _target;
    private readonly FlowController _flowController;
    private readonly ConnectionPoolContext _poolContext;
    private readonly CancellationTokenSource _connectionCts;
    private readonly int _chunkSize;
    private readonly int _hardCap;

    private readonly Queue<int> _readyQueue = new();
    private readonly Dictionary<int, BodyDrainSlot<int>> _activeSlots = new();
    private readonly HashSet<int> _cancelledStreams = new();
    private readonly HashSet<int> _windowBlockedStreams = new();
    private readonly HashSet<int> _limboSlots = new();

    private int _readSlots = 2;
    private int _asyncInFlight;

    public FlowControlledBodyPump(
        IBodyDrainTarget<int> target,
        FlowController flowController,
        ConnectionPoolContext poolContext,
        CancellationTokenSource connectionCts,
        int chunkSize,
        int hardCap)
    {
        _target = target;
        _flowController = flowController;
        _poolContext = poolContext;
        _connectionCts = connectionCts;
        _chunkSize = chunkSize;
        _hardCap = hardCap;
    }

    public void Register(int streamId, Stream bodyStream, long? contentLength, CancellationToken requestCt)
    {
        var slot = _poolContext.Rent(static () => new BodyDrainSlot<int>());
        var linkedCts = requestCt.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(_connectionCts.Token, requestCt)
            : CancellationTokenSource.CreateLinkedTokenSource(_connectionCts.Token);
        slot.Initialize(streamId, bodyStream, contentLength, requestCt, linkedCts);
        slot.EnsureBuffer(_chunkSize);
        _activeSlots[streamId] = slot;

        if (_flowController.GetStreamSendWindow(streamId) > 0)
        {
            _readyQueue.Enqueue(streamId);
            TryScheduleReads();
        }
        else
        {
            _windowBlockedStreams.Add(streamId);
        }
    }

    public void RegisterWithLimbo(int streamId, ReadOnlyMemory<byte> data, CancellationToken requestCt)
    {
        var slot = _poolContext.Rent(static () => new BodyDrainSlot<int>());
        var linkedCts = requestCt.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(_connectionCts.Token, requestCt)
            : CancellationTokenSource.CreateLinkedTokenSource(_connectionCts.Token);
        slot.Initialize(streamId, null!, null, requestCt, linkedCts);

        // EnsureBuffer rents from MemoryPool (slot is fresh after pool return, Buffer is null).
        // Copy remainder data into the slot's buffer so limbo slice points into owned memory.
        slot.EnsureBuffer(Math.Max(data.Length, _chunkSize));
        data.CopyTo(slot.Buffer!.Memory);
        slot.StoreLimbo(slot.Buffer.Memory[..data.Length]);

        // No body stream — limbo is the entire remaining payload; mark as complete so
        // DrainLimboSlot emits END_STREAM and calls CompleteDrain after the flush.
        slot.MarkDrainComplete();

        _activeSlots[streamId] = slot;
        _limboSlots.Add(streamId);
    }

    public void OnWindowUpdate(int streamId)
    {
        if (streamId == 0)
        {
            // Connection-level: drain all limbo slots, unblock all window-blocked streams
            var limboSnapshot = _limboSlots.ToArray();
            foreach (var id in limboSnapshot)
            {
                if (_activeSlots.TryGetValue(id, out var slot))
                {
                    DrainLimboSlot(slot);
                }
            }

            foreach (var id in _windowBlockedStreams)
            {
                _readyQueue.Enqueue(id);
            }

            _windowBlockedStreams.Clear();
        }
        else
        {
            if (_windowBlockedStreams.Remove(streamId))
            {
                _readyQueue.Enqueue(streamId);
            }
            else if (_limboSlots.Contains(streamId))
            {
                if (_activeSlots.TryGetValue(streamId, out var slot))
                {
                    DrainLimboSlot(slot);
                }
            }
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

        slot.CompleteAsyncRead();

        if (slot.IsOrphaned)
        {
            RemoveAndReturnSlot(streamId, slot);
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

        slot.CompleteAsyncRead();

        if (slot.IsOrphaned)
        {
            RemoveAndReturnSlot(streamId, slot);
            return;
        }

        _limboSlots.Remove(streamId);
        _activeSlots.Remove(streamId);
        _target.OnDrainFailed(streamId, reason);
        slot.DisposeResources();
        _poolContext.Return(slot);
    }

    public void HandleDrainContinue(int streamId)
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

        if (_limboSlots.Remove(streamId))
        {
            RemoveAndReturnSlot(streamId, slot);
            return;
        }

        if (_windowBlockedStreams.Remove(streamId))
        {
            RemoveAndReturnSlot(streamId, slot);
            return;
        }

        // In ready queue: use lazy removal via _cancelledStreams
        _cancelledStreams.Add(streamId);
    }

    public void Cleanup()
    {
        _connectionCts.Cancel();

        foreach (var (streamId, slot) in _activeSlots)
        {
            if (!slot.IsOrphaned)
            {
                slot.DisposeResources();
                _poolContext.Return(slot);
            }

            _limboSlots.Remove(streamId);
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

        var windowSlots = connWindow >= _chunkSize
            ? (int)Math.Min(connWindow / _chunkSize, _hardCap)
            : 1;
        var effectiveSlots = Math.Min(Math.Min(_readSlots, windowSlots), _hardCap);

        while (_asyncInFlight < effectiveSlots && _readyQueue.Count > 0)
        {
            var streamId = _readyQueue.Dequeue();

            if (_cancelledStreams.Remove(streamId))
            {
                if (_activeSlots.TryGetValue(streamId, out var cancelled))
                {
                    RemoveAndReturnSlot(streamId, cancelled);
                }

                continue;
            }

            if (!_activeSlots.TryGetValue(streamId, out var slot))
            {
                continue;
            }

            if (_flowController.GetStreamSendWindow(streamId) <= 0)
            {
                _windowBlockedStreams.Add(streamId);
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

    private void ProcessReadResult(BodyDrainSlot<int> slot, int bytesRead)
    {
        if (bytesRead == 0)
        {
            if (slot.HasLimbo)
            {
                slot.MarkDrainComplete();
                DrainLimboSlot(slot);
            }
            else
            {
                _target.EmitDataFrames(slot.StreamId, default, endStream: true);
                CompleteDrain(slot);
            }

            return;
        }

        var streamWindow = _flowController.GetStreamSendWindow(slot.StreamId);
        var connWindow = _flowController.ConnectionSendWindow;
        var window = (int)Math.Min(streamWindow, connWindow);
        var data = slot.Buffer!.Memory[..bytesRead];

        if (window >= bytesRead)
        {
            _target.EmitDataFrames(slot.StreamId, data, endStream: false);
            _flowController.OnDataSent(slot.StreamId, bytesRead);
            _readSlots = Math.Min(_readSlots + 1, _hardCap);
            _readyQueue.Enqueue(slot.StreamId);
            TryScheduleReads();
        }
        else if (window > 0)
        {
            _target.EmitDataFrames(slot.StreamId, data[..window], endStream: false);
            _flowController.OnDataSent(slot.StreamId, window);
            slot.StoreLimbo(data[window..]);
            _limboSlots.Add(slot.StreamId);

            if (connWindow <= window)
            {
                _readSlots = Math.Max(_readSlots / 2, 1);
            }
        }
        else
        {
            slot.StoreLimbo(data);
            _limboSlots.Add(slot.StreamId);

            if (connWindow == 0)
            {
                _readSlots = Math.Max(_readSlots / 2, 1);
            }
        }
    }

    private void DrainLimboSlot(BodyDrainSlot<int> slot)
    {
        var connWindow = _flowController.ConnectionSendWindow;
        var streamWindow = slot.BodyStream is null
            ? connWindow
            : _flowController.GetStreamSendWindow(slot.StreamId);
        var window = (int)Math.Min(streamWindow, connWindow);

        if (window <= 0)
        {
            return;
        }

        if (window >= slot.LimboData.Length)
        {
            _target.EmitDataFrames(slot.StreamId, slot.LimboData, endStream: slot.IsDrainComplete);
            _flowController.OnDataSent(slot.StreamId, slot.LimboData.Length);
            _limboSlots.Remove(slot.StreamId);
            slot.ClearLimbo();

            if (slot.IsDrainComplete)
            {
                CompleteDrain(slot);
            }
            else
            {
                _readyQueue.Enqueue(slot.StreamId);
            }
        }
        else
        {
            _target.EmitDataFrames(slot.StreamId, slot.LimboData[..window], endStream: false);
            _flowController.OnDataSent(slot.StreamId, window);
            slot.ShrinkLimbo(window);
        }
    }

    private void CompleteDrain(BodyDrainSlot<int> slot)
    {
        _limboSlots.Remove(slot.StreamId);
        _activeSlots.Remove(slot.StreamId);
        _target.OnDrainComplete(slot.StreamId);
        slot.DisposeResources();
        _poolContext.Return(slot);
    }

    private void RemoveAndReturnSlot(int streamId, BodyDrainSlot<int> slot)
    {
        _activeSlots.Remove(streamId);
        slot.DisposeResources();
        _poolContext.Return(slot);
    }
}
