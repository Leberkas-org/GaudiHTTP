using System.Buffers;
using Akka.Actor;
using GaudiHTTP.Pooling;
using GaudiHTTP.Protocol.Syntax.Http2;

namespace GaudiHTTP.Protocol.Body;

internal sealed class BodyDrainScheduler
{
    private const int MaxSyncReadsPerDispatch = 64;

    private readonly IBodyDrainTarget _target;
    private readonly FlowController _flowController;
    private readonly CancellationTokenSource _connectionCts;
    private readonly ConnectionPoolContext _poolContext;
    private readonly int _chunkSize;
    private readonly int _hardCap;

    private readonly Queue<int> _readyQueue = new();
    private readonly Dictionary<int, DrainSlot> _activeSlots = new();
    private readonly HashSet<int> _cancelledStreams = new();
    private readonly HashSet<int> _windowBlockedStreams = new();
    private readonly HashSet<int> _limboSlots = new();

    private int _readSlots = 2;
    private int _asyncInFlight;

    public BodyDrainScheduler(
        IBodyDrainTarget target,
        FlowController flowController,
        CancellationTokenSource connectionCts,
        ConnectionPoolContext poolContext,
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
        var slot = _poolContext.Rent(static () => new DrainSlot());
        slot.Initialize(streamId, bodyStream, requestCt, linkedCts);
        slot.ContentLength = contentLength;
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
        var linkedCts = requestCt.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(_connectionCts.Token, requestCt)
            : null;
        var slot = _poolContext.Rent(static () => new DrainSlot());

        // Initialize sets StreamId, RequestCt, LinkedCts and cached transforms;
        // BodyStream is intentionally left null for limbo-only slots.
        slot.StreamId = streamId;
        slot.RequestCt = requestCt;
        slot.LinkedCts = linkedCts;

        // Rent a buffer and copy remainder data into it so limbo slice points into our buffer
        var buffer = MemoryPool<byte>.Shared.Rent(Math.Max(data.Length, _chunkSize));
        slot.Buffer = buffer;
        data.CopyTo(buffer.Memory);
        slot.StoreLimbo(buffer.Memory[..data.Length]);

        // No body stream — limbo is the entire remaining payload; mark as complete so
        // DrainLimboSlot emits END_STREAM and calls CompleteDrain after the flush.
        slot.IsDrainComplete = true;

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

        slot.IsReadInFlight = false;
        slot.ConsecutiveSyncReads = 0;

        if (slot.IsOrphaned)
        {
            DisposeSlotResources(slot);
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

        slot.IsReadInFlight = false;
        slot.ConsecutiveSyncReads = 0;

        if (slot.IsOrphaned)
        {
            DisposeSlotResources(slot);
            _poolContext.Return(slot);
            _activeSlots.Remove(streamId);
            return;
        }

        _limboSlots.Remove(streamId);
        _activeSlots.Remove(streamId);
        _target.OnDrainFailed(streamId, reason);
        DisposeSlotResources(slot);
        _poolContext.Return(slot);
    }

    public void HandleDrainContinue(int streamId)
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
            // Async read in flight: mark orphaned, cleanup happens in HandleReadComplete/Failed
            slot.IsOrphaned = true;
            return;
        }

        if (_limboSlots.Remove(streamId))
        {
            // Has limbo data but no in-flight read: clean up immediately
            _activeSlots.Remove(streamId);
            DisposeSlotResources(slot);
            _poolContext.Return(slot);
            return;
        }

        if (_windowBlockedStreams.Remove(streamId))
        {
            _activeSlots.Remove(streamId);
            DisposeSlotResources(slot);
            _poolContext.Return(slot);
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
                DisposeSlotResources(slot);
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

        // If connection window is fully exhausted, nothing can be sent
        if (connWindow <= 0)
        {
            return;
        }

        // connWindow / chunkSize gives how many full chunks fit in the connection window.
        // If connWindow is positive but smaller than chunkSize, allow at least 1 slot —
        // we'll send what we can and store the remainder as limbo.
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
                    _activeSlots.Remove(streamId);
                    DisposeSlotResources(cancelled);
                    _poolContext.Return(cancelled);
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

            // Starvation guard: yield after MaxSyncReadsPerDispatch consecutive sync reads so
            // the actor thread can process other messages between bursts.
            if (slot.ConsecutiveSyncReads >= MaxSyncReadsPerDispatch)
            {
                slot.ConsecutiveSyncReads = 0;
                // Use IsReadInFlight as a yield-in-progress marker so re-entrant calls from
                // ProcessReadResult cannot start another read while waiting for HandleDrainContinue.
                slot.IsReadInFlight = true;
                _target.StageActor.Tell(new DrainContinue(slot.StreamId), ActorRefs.NoSender);
                continue;
            }

            if (slot.Buffer is null)
            {
                slot.Buffer = MemoryPool<byte>.Shared.Rent(Math.Max(_chunkSize, 256));
            }

            StartRead(slot);
        }
    }

    private void StartRead(DrainSlot slot)
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
        vt.PipeTo(
            _target.StageActor,
            success: slot.CachedSuccessTransform,
            failure: slot.CachedFailureTransform);
    }

    private void ProcessReadResult(DrainSlot slot, int bytesRead)
    {
        if (bytesRead == 0)
        {
            if (slot.HasLimbo)
            {
                // Drain limbo first, then emit END_STREAM
                slot.IsDrainComplete = true;
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

    private void DrainLimboSlot(DrainSlot slot)
    {
        var connWindow = _flowController.ConnectionSendWindow;
        // For RegisterWithLimbo slots (no body stream), the stream window was already
        // satisfied when the fast-path partial send was made — only check connection window.
        // For regular drain slots, check both windows.
        var streamWindow = slot.BodyStream is null
            ? connWindow
            : _flowController.GetStreamSendWindow(slot.StreamId);
        var window = (int)Math.Min(streamWindow, connWindow);

        if (window <= 0)
        {
            // Still blocked — stays in _limboSlots
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
            // Shrink slice in-place — still no copy (offset into same buffer)
            slot.LimboData = slot.LimboData[window..];
        }
    }

    private void CompleteDrain(DrainSlot slot)
    {
        _limboSlots.Remove(slot.StreamId);
        _activeSlots.Remove(slot.StreamId);
        _target.OnDrainComplete(slot.StreamId);
        DisposeSlotResources(slot);
        _poolContext.Return(slot);
    }

    private static void DisposeSlotResources(DrainSlot slot)
    {
        slot.Buffer?.Dispose();
        slot.LinkedCts?.Dispose();
    }
}
