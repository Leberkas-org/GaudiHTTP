using TurboHTTP.Pooling;

namespace TurboHTTP.Protocol.Body;

internal abstract class BodyPumpBase<TStreamId> where TStreamId : notnull
{
    private const int MinBudget = 8;
    private const int MaxBudget = 48;
    private const double Alpha = 0.3;
    private const long SlowThresholdTicks = TimeSpan.TicksPerMillisecond * 10;
    private const long FastThresholdTicks = TimeSpan.TicksPerMillisecond / 2;

    private readonly IBodyDrainTarget<TStreamId> _target;
    private readonly ConnectionPoolContext _poolContext;
    private readonly CancellationTokenSource _connectionCts;

    private readonly Queue<TStreamId> _readyQueue = new();
    private readonly Dictionary<TStreamId, BodyDrainSlot<TStreamId>> _activeSlots = new();
    private readonly HashSet<TStreamId> _cancelledStreams = new();

    private int _credits;
    private int _budget;
    private double _ema;
    private long _lastPullTicks;

    protected BodyPumpBase(
        IBodyDrainTarget<TStreamId> target,
        ConnectionPoolContext poolContext,
        CancellationTokenSource connectionCts)
    {
        _target = target;
        _poolContext = poolContext;
        _connectionCts = connectionCts;
        _ema = (SlowThresholdTicks + FastThresholdTicks) / 2.0;
        _budget = MapBudget(_ema);
        _lastPullTicks = Environment.TickCount64 * TimeSpan.TicksPerMillisecond;
    }

    public void AddCredit()
    {
        UpdateEma();
        _credits = Math.Min(_credits + 1, MaxBudget);
        TryStartReadRound();
        if (_credits > 0 && _readyQueue.Count > 0)
        {
            TryReadNextEligible();
        }
    }

    public void Register(TStreamId streamId, Stream bodyStream, long? contentLength, CancellationToken requestCt)
    {
        var slot = RentSlot();
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(requestCt, _connectionCts.Token);
        slot.Initialize(streamId, bodyStream, contentLength, requestCt, linkedCts);
        slot.EnsureBuffer(_target.PreferredChunkSize);
        _activeSlots[streamId] = slot;
        EnqueueStream(streamId);

        if (_target.HasPendingDemand)
        {
            TryReadNextEligible();
        }
    }

    public void HandleReadComplete(TStreamId streamId, int bytesRead)
    {
        if (!_activeSlots.TryGetValue(streamId, out var slot))
        {
            return;
        }

        slot.CompleteAsyncRead();

        if (slot.IsOrphaned)
        {
            CleanupSlot(streamId, slot);
            return;
        }

        ProcessReadResult(streamId, slot, bytesRead);

        if (_credits > 0)
        {
            TryReadNextEligible();
        }
    }

    public void HandleReadFailed(TStreamId streamId, Exception reason)
    {
        if (!_activeSlots.TryGetValue(streamId, out var slot))
        {
            return;
        }

        slot.CompleteAsyncRead();

        if (slot.IsOrphaned)
        {
            CleanupSlot(streamId, slot);
            return;
        }

        _target.OnDrainFailed(streamId, reason);
        CleanupSlot(streamId, slot);
    }

    public void Cancel(TStreamId streamId)
    {
        if (!_activeSlots.TryGetValue(streamId, out var slot))
        {
            return;
        }

        if (slot.IsReadInFlight)
        {
            slot.MarkOrphaned();
            slot.LinkedCts?.Cancel();
            _cancelledStreams.Add(streamId);
        }
        else
        {
            CleanupSlot(streamId, slot);
        }

        OnStreamCancelled(streamId);
    }

    public void CancelAll()
    {
        _connectionCts.Cancel();

        foreach (var (_, slot) in _activeSlots)
        {
            if (slot.IsReadInFlight)
            {
                slot.MarkOrphaned();
            }
            else
            {
                DisposeSlot(slot);
            }
        }

        _activeSlots.Clear();
        _readyQueue.Clear();
        _cancelledStreams.Clear();
        _credits = 0;
        OnCancelAll();
    }

    // Virtual hooks for subclasses
    protected virtual void OnCancelAll() { }

    protected virtual void OnStreamCancelled(TStreamId streamId) { }

    // When returning false, the override is responsible for tracking the stream
    // (e.g., moving it to a blocked set). The stream is removed from the ready queue.
    protected virtual bool IsStreamEligible(TStreamId streamId, BodyDrainSlot<TStreamId> slot) => true;

    protected virtual int ComputeReadSize(TStreamId streamId, BodyDrainSlot<TStreamId> slot)
        => _target.PreferredChunkSize;

    protected virtual void BeforeRead(TStreamId streamId, BodyDrainSlot<TStreamId> slot) { }

    protected virtual void AfterRead(TStreamId streamId, BodyDrainSlot<TStreamId> slot, int bytesRead) { }

    protected virtual BodyDrainSlot<TStreamId> RentSlot()
        => _poolContext.Rent(() => new BodyDrainSlot<TStreamId>());

    protected virtual void ReturnSlot(BodyDrainSlot<TStreamId> slot)
        => _poolContext.Return(slot);

    protected virtual void EnqueueStream(TStreamId streamId)
        => _readyQueue.Enqueue(streamId);

    private void TryStartReadRound()
    {
        var threshold = Math.Max(Math.Min(_budget / 2, _activeSlots.Count), 1);

        if (_credits < threshold)
        {
            return;
        }

        var reads = 0;
        var queueSize = _readyQueue.Count;
        while (reads < _budget && _credits > 0 && queueSize-- > 0)
        {
            if (!_readyQueue.TryDequeue(out var streamId))
            {
                break;
            }

            if (_cancelledStreams.Remove(streamId))
            {
                continue;
            }

            if (!_activeSlots.TryGetValue(streamId, out var slot))
            {
                continue;
            }

            if (slot.IsReadInFlight)
            {
                _readyQueue.Enqueue(streamId);
                continue;
            }

            if (!IsStreamEligible(streamId, slot))
            {
                continue;
            }

            PerformRead(streamId, slot);
            reads++;
        }
    }

    private void TryReadNextEligible()
    {
        var queueSize = _readyQueue.Count;
        while (queueSize-- > 0)
        {
            if (!_readyQueue.TryDequeue(out var streamId))
            {
                return;
            }

            if (_cancelledStreams.Remove(streamId))
            {
                continue;
            }

            if (!_activeSlots.TryGetValue(streamId, out var slot))
            {
                continue;
            }

            if (slot.IsReadInFlight)
            {
                _readyQueue.Enqueue(streamId);
                continue;
            }

            if (!IsStreamEligible(streamId, slot))
            {
                continue;
            }

            PerformRead(streamId, slot);
            return;
        }
    }

    private void PerformRead(TStreamId streamId, BodyDrainSlot<TStreamId> slot)
    {
        if (slot.Buffer!.Memory.Length < _target.PreferredChunkSize)
        {
            slot.ReplaceBuffer(_target.PreferredChunkSize);
        }

        var readSize = ComputeReadSize(streamId, slot);
        BeforeRead(streamId, slot);
        var result = BodyPumpHelper.StartRead(slot, readSize, _target.PipeToTarget);
        _credits--;

        if (result.Outcome == BodyPumpHelper.ReadOutcome.CompletedSynchronously)
        {
            ProcessReadResult(streamId, slot, result.BytesRead);
        }
    }

    private void ProcessReadResult(TStreamId streamId, BodyDrainSlot<TStreamId> slot, int bytesRead)
    {
        AfterRead(streamId, slot, bytesRead);

        if (bytesRead == 0)
        {
            _target.EmitDataFrames(streamId, default, endStream: true);
            _target.OnDrainComplete(streamId);
            CleanupSlot(streamId, slot);
            return;
        }

        _readyQueue.Enqueue(streamId);
        _target.EmitDataFrames(streamId, slot.Buffer!.Memory[..bytesRead], endStream: false);
    }

    private void UpdateEma()
    {
        var nowTicks = Environment.TickCount64 * TimeSpan.TicksPerMillisecond;
        if (_lastPullTicks > 0)
        {
            var interval = nowTicks - _lastPullTicks;
            _ema = Alpha * interval + (1 - Alpha) * _ema;
            _budget = MapBudget(_ema);
        }

        _lastPullTicks = nowTicks;
    }

    private static int MapBudget(double ema)
    {
        if (ema <= FastThresholdTicks)
        {
            return MaxBudget;
        }

        if (ema >= SlowThresholdTicks)
        {
            return MinBudget;
        }

        var ratio = (ema - FastThresholdTicks) / (SlowThresholdTicks - FastThresholdTicks);
        return MaxBudget - (int)(ratio * (MaxBudget - MinBudget));
    }

    private void CleanupSlot(TStreamId streamId, BodyDrainSlot<TStreamId> slot)
    {
        _activeSlots.Remove(streamId);
        DisposeSlot(slot);
    }

    private void DisposeSlot(BodyDrainSlot<TStreamId> slot)
    {
        slot.DisposeResources();
        slot.Reset();
        ReturnSlot(slot);
    }

    // Protected accessors for subclasses and testing
    protected int GetCredits() => _credits;
    protected int GetBudget() => _budget;
    protected int GetActiveStreamCount() => _activeSlots.Count;
    protected ConnectionPoolContext PoolContext => _poolContext;
    protected IBodyDrainTarget<TStreamId> Target => _target;
}
