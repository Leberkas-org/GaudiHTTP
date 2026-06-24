using Akka.Actor;
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

        var threshold = Math.Max(Math.Min(_budget / 2, _activeSlots.Count), 1);
        if (_credits >= threshold)
        {
            DrainReady(_budget);
        }
        else if (_credits > 0 && _readyQueue.Count > 0)
        {
            DrainReady(1);
        }
    }

    public void Register(TStreamId streamId, Stream bodyStream, CancellationToken requestCt, int initialCredits = 0)
    {
        var slot = _poolContext.Rent(static () => new BodyDrainSlot<TStreamId>());
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(requestCt, _connectionCts.Token);
        slot.Initialize(streamId, bodyStream, requestCt, linkedCts);
        slot.EnsureBuffer(_target.PreferredChunkSize);
        _activeSlots[streamId] = slot;
        EnqueueStream(streamId);

        if (_target.HasPendingDemand)
        {
            _credits++;
            DrainReady(1);
        }

        for (var i = 0; i < initialCredits; i++)
        {
            AddCredit();
        }
    }

    public void HandleReadComplete(TStreamId streamId, int bytesRead)
    {
        if (!_activeSlots.TryGetValue(streamId, out var slot))
        {
            return;
        }

        slot.CompleteRead();

        if (slot.IsOrphaned)
        {
            AfterRead(streamId, slot, 0);
            CleanupSlot(streamId, slot);
            return;
        }

        ProcessReadResult(streamId, slot, bytesRead);

        if (_credits > 0)
        {
            DrainReady(1);
        }
    }

    public void HandleReadFailed(TStreamId streamId, Exception reason)
    {
        if (!_activeSlots.TryGetValue(streamId, out var slot))
        {
            return;
        }

        slot.CompleteRead();

        if (slot.IsOrphaned)
        {
            AfterRead(streamId, slot, 0);
            CleanupSlot(streamId, slot);
            return;
        }

        AfterRead(streamId, slot, 0);

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

    // H2 flow-control extension points — only FlowControlledBodyPump overrides these
    protected virtual void OnCancelAll() { }

    protected virtual void OnStreamCancelled(TStreamId streamId) { }

    // When returning false, the override is responsible for tracking the stream
    // (e.g., moving it to a blocked set). The stream is removed from the ready queue.
    protected virtual bool IsStreamEligible(TStreamId streamId, BodyDrainSlot<TStreamId> slot) => true;

    protected virtual int ComputeReadSize(TStreamId streamId, BodyDrainSlot<TStreamId> slot)
        => _target.PreferredChunkSize;

    protected virtual void BeforeRead(TStreamId streamId, BodyDrainSlot<TStreamId> slot) { }

    protected virtual void AfterRead(TStreamId streamId, BodyDrainSlot<TStreamId> slot, int bytesRead) { }

    protected virtual void EnqueueStream(TStreamId streamId)
        => _readyQueue.Enqueue(streamId);

    private void DrainReady(int maxReads)
    {
        var reads = 0;
        var queueSize = _readyQueue.Count;
        while (reads < maxReads && _credits > 0 && queueSize-- > 0)
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

    private void PerformRead(TStreamId streamId, BodyDrainSlot<TStreamId> slot)
    {
        if (slot.Buffer!.Memory.Length < _target.PreferredChunkSize)
        {
            slot.ReplaceBuffer(_target.PreferredChunkSize);
        }

        var readSize = ComputeReadSize(streamId, slot);
        BeforeRead(streamId, slot);
        var result = StartRead(slot, readSize, _target.PipeToTarget);
        _credits--;

        if (result.Outcome == ReadOutcome.CompletedSynchronously)
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
            // Clean up the slot BEFORE notifying the target. OnDrainComplete triggers
            // CloseStream which calls _pump.Cancel(streamId). If the slot is still in
            // _activeSlots, Cancel will CleanupSlot a second time, double-returning it
            // to the pool. A later rent then shares the slot with another stream, and
            // the stale cleanup nulls its Buffer, causing a NullReferenceException.
            CleanupSlot(streamId, slot);
            _target.OnDrainComplete(streamId);
            return;
        }

        _readyQueue.Enqueue(streamId);
        _target.EmitDataFrames(streamId, slot.Buffer!.Memory[..bytesRead], endStream: false);
    }

    private void UpdateEma()
    {
        var nowTicks = Environment.TickCount64 * TimeSpan.TicksPerMillisecond;
        var interval = nowTicks - _lastPullTicks;
        _ema = Alpha * interval + (1 - Alpha) * _ema;
        _budget = MapBudget(_ema);
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

    private static ReadResult StartRead(
        BodyDrainSlot<TStreamId> slot,
        int chunkSize,
        IActorRef pipeToTarget)
    {
        slot.BeginRead();
        var token = slot.LinkedCts?.Token ?? slot.RequestCt;
        var vt = slot.BodyStream!.ReadAsync(slot.Buffer!.Memory[..chunkSize], token);

        if (vt.IsCompletedSuccessfully)
        {
            slot.CompleteRead();
            return new ReadResult(ReadOutcome.CompletedSynchronously, vt.Result);
        }

        vt.PipeTo(
            pipeToTarget,
            success: slot.CachedSuccessTransform,
            failure: slot.CachedFailureTransform);
        return new ReadResult(ReadOutcome.Dispatched, 0);
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
        _poolContext.Return(slot);
    }

    private readonly record struct ReadResult(ReadOutcome Outcome, int BytesRead);

    private enum ReadOutcome
    {
        CompletedSynchronously,
        Dispatched
    }

    // Protected accessors for subclasses and testing
    protected int GetCredits() => _credits;
    protected int GetBudget() => _budget;
    protected int GetActiveStreamCount() => _activeSlots.Count;
    protected ConnectionPoolContext PoolContext => _poolContext;
    protected IBodyDrainTarget<TStreamId> Target => _target;
}
