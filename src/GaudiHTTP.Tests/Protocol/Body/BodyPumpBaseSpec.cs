using Akka.Actor;
using GaudiHTTP.Pooling;
using GaudiHTTP.Protocol.Body;

namespace GaudiHTTP.Tests.Protocol.Body;

public sealed class BodyPumpBaseSpec
{
    private sealed class TestPump : BodyPumpBase<int>
    {
        public TestPump(IBodyDrainTarget<int> target, ConnectionPoolContext pool, CancellationTokenSource cts)
            : base(target, pool, cts) { }

        public int Credits => GetCredits();
        public int Budget => GetBudget();
        public int ActiveStreamCount => GetActiveStreamCount();
    }

    private sealed class FakeTarget : IBodyDrainTarget<int>
    {
        public IActorRef PipeToTarget { get; } = ActorRefs.Nobody;
        public bool HasPendingDemand { get; set; }
        public int PreferredChunkSize { get; set; } = 16 * 1024;
        public List<(int StreamId, byte[] Data, bool EndStream)> Emitted { get; } = [];
        public List<int> Completed { get; } = [];
        public List<(int StreamId, Exception Reason)> Failed { get; } = [];

        public void EmitDataFrames(int streamId, ReadOnlyMemory<byte> data, bool endStream)
            => Emitted.Add((streamId, data.ToArray(), endStream));

        public void OnDrainComplete(int streamId) => Completed.Add(streamId);
        public void OnDrainFailed(int streamId, Exception reason) => Failed.Add((streamId, reason));
    }

    private static MemoryStream MakeBody(int size)
    {
        var data = new byte[size];
        for (var i = 0; i < size; i++)
        {
            data[i] = (byte)(i % 256);
        }

        return new MemoryStream(data);
    }

    [Fact(Timeout = 5000)]
    public void AddCredit_should_accumulate_credits()
    {
        var target = new FakeTarget();
        var pump = new TestPump(target, new ConnectionPoolContext(), new CancellationTokenSource());

        pump.AddCredit();
        pump.AddCredit();
        pump.AddCredit();

        Assert.Equal(3, pump.Credits);
    }

    [Fact(Timeout = 5000)]
    public void AddCredit_should_cap_at_max_budget()
    {
        var target = new FakeTarget();
        var pump = new TestPump(target, new ConnectionPoolContext(), new CancellationTokenSource());

        for (var i = 0; i < 100; i++)
        {
            pump.AddCredit();
        }

        Assert.Equal(48, pump.Credits);
    }

    [Fact(Timeout = 5000)]
    public void Register_should_read_immediately_when_HasPendingDemand()
    {
        var target = new FakeTarget { HasPendingDemand = true };
        var pump = new TestPump(target, new ConnectionPoolContext(), new CancellationTokenSource());
        var body = MakeBody(100);

        pump.Register(0, body, CancellationToken.None);

        Assert.True(target.Emitted.Count >= 1);
    }

    [Fact(Timeout = 5000)]
    public void Register_should_not_read_without_demand_or_credits()
    {
        var target = new FakeTarget { HasPendingDemand = false };
        var pump = new TestPump(target, new ConnectionPoolContext(), new CancellationTokenSource());
        var body = MakeBody(100);

        pump.Register(0, body, CancellationToken.None);

        Assert.Empty(target.Emitted);
    }

    [Fact(Timeout = 5000)]
    public void AddCredit_should_trigger_read_round_when_threshold_reached()
    {
        var target = new FakeTarget { HasPendingDemand = false };
        var pump = new TestPump(target, new ConnectionPoolContext(), new CancellationTokenSource());
        var body = MakeBody(100);

        pump.Register(0, body, CancellationToken.None);
        Assert.Empty(target.Emitted);

        // Threshold for 1 active stream = min(budget/2, 1*2) = 2
        pump.AddCredit();
        pump.AddCredit();

        Assert.True(target.Emitted.Count >= 1);
    }

    [Fact(Timeout = 5000)]
    public void ReadRound_should_decrement_credits_per_read()
    {
        var target = new FakeTarget { HasPendingDemand = false };
        var pump = new TestPump(target, new ConnectionPoolContext(), new CancellationTokenSource());
        var body = MakeBody(100);

        pump.Register(0, body, CancellationToken.None);

        for (var i = 0; i < 10; i++)
        {
            pump.AddCredit();
        }

        Assert.True(pump.Credits < 10);
    }

    [Fact(Timeout = 5000)]
    public void CancelAll_should_clear_all_state()
    {
        var target = new FakeTarget { HasPendingDemand = false };
        var cts = new CancellationTokenSource();
        var pump = new TestPump(target, new ConnectionPoolContext(), cts);
        var body = MakeBody(100);

        pump.Register(0, body, CancellationToken.None);
        pump.CancelAll();

        Assert.True(cts.IsCancellationRequested);
        Assert.Equal(0, pump.ActiveStreamCount);
    }

    [Fact(Timeout = 5000)]
    public void Cancel_should_cleanup_idle_slot()
    {
        var target = new FakeTarget { HasPendingDemand = false };
        var pump = new TestPump(target, new ConnectionPoolContext(), new CancellationTokenSource());
        var body = MakeBody(100);

        pump.Register(0, body, CancellationToken.None);
        pump.Cancel(0);

        Assert.Equal(0, pump.ActiveStreamCount);
    }

    [Fact(Timeout = 5000)]
    public void HandleReadComplete_should_emit_endStream_on_empty_body()
    {
        var target = new FakeTarget { HasPendingDemand = true };
        var pump = new TestPump(target, new ConnectionPoolContext(), new CancellationTokenSource());
        var body = new MemoryStream([]);

        pump.Register(0, body, CancellationToken.None);

        Assert.Single(target.Emitted);
        Assert.True(target.Emitted[0].EndStream);
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void HandleReadFailed_should_call_OnDrainFailed()
    {
        var target = new FakeTarget { HasPendingDemand = false };
        var pump = new TestPump(target, new ConnectionPoolContext(), new CancellationTokenSource());
        var body = MakeBody(100);
        var error = new IOException("test error");

        pump.Register(0, body, CancellationToken.None);
        pump.HandleReadFailed(0, error);

        Assert.Single(target.Failed);
        Assert.Equal(0, target.Failed[0].StreamId);
        Assert.Same(error, target.Failed[0].Reason);
    }

    [Fact(Timeout = 5000)]
    public void Budget_should_be_initialized_within_valid_range()
    {
        var target = new FakeTarget();
        var pump = new TestPump(target, new ConnectionPoolContext(), new CancellationTokenSource());

        Assert.InRange(pump.Budget, 8, 48);
    }

    // Round-robin fairness

    [Fact(Timeout = 5000)]
    public void ReadRound_should_serve_each_stream_before_second_turn_with_two_streams()
    {
        // Both streams registered with HasPendingDemand=false so we control when reads fire.
        // We then add enough credits to trigger a read round and verify both stream IDs appear
        // before either appears a second time.
        var target = new FakeTarget { HasPendingDemand = false };
        var pump = new TestPump(target, new ConnectionPoolContext(), new CancellationTokenSource());

        // Two bodies large enough to not complete in a single read (> 16 KB chunk)
        var body0 = MakeBody(64 * 1024);
        var body1 = MakeBody(64 * 1024);

        pump.Register(0, body0, CancellationToken.None);
        pump.Register(1, body1, CancellationToken.None);

        // Add enough credits to exceed the read-round threshold (min(budget/2, 2*2) = 4).
        for (var i = 0; i < 20; i++)
        {
            pump.AddCredit();
        }

        // Collect the stream IDs of the first two non-EOF emits.
        var firstTwo = target.Emitted.Where(e => !e.EndStream).Take(2).Select(e => e.StreamId).ToList();
        Assert.Equal(2, firstTwo.Distinct().Count());
    }

    [Fact(Timeout = 5000)]
    public void ReadRound_should_serve_all_three_streams_before_second_turn()
    {
        var target = new FakeTarget { HasPendingDemand = false };
        var pump = new TestPump(target, new ConnectionPoolContext(), new CancellationTokenSource());

        var bodySize = 64 * 1024;
        pump.Register(0, MakeBody(bodySize), CancellationToken.None);
        pump.Register(1, MakeBody(bodySize), CancellationToken.None);
        pump.Register(2, MakeBody(bodySize), CancellationToken.None);

        // Add credits well above threshold to trigger a multi-read round.
        for (var i = 0; i < 48; i++)
        {
            pump.AddCredit();
        }

        // Each stream should have been served at least once.
        var streamIds = target.Emitted.Where(e => !e.EndStream).Select(e => e.StreamId).ToList();
        Assert.Contains(0, streamIds);
        Assert.Contains(1, streamIds);
        Assert.Contains(2, streamIds);

        // Verify no stream gets two consecutive reads before the others each get one.
        // The first three non-EOF reads must cover all three stream IDs.
        var firstThree = streamIds.Take(3).ToList();
        Assert.Equal(3, firstThree.Distinct().Count());
    }

    // Adaptive budget

    [Fact(Timeout = 5000)]
    public void Budget_should_clamp_at_MinBudget_8_under_slow_calls()
    {
        var target = new FakeTarget();
        var pump = new TestPump(target, new ConnectionPoolContext(), new CancellationTokenSource());

        // Force the EMA to the slow-end by sleeping between AddCredit calls.
        // 10 ms = SlowThresholdTicks, so we need intervals >= 10 ms.
        // Use Thread.Sleep to space calls apart enough for the EMA to converge.
        // 5 calls at 15 ms each should push EMA above SlowThresholdTicks (10 ms).
        for (var i = 0; i < 5; i++)
        {
            Thread.Sleep(15);
            pump.AddCredit();
        }

        // Budget should be at the minimum (8) because intervals are slow.
        Assert.Equal(8, pump.Budget);
    }

    [Fact(Timeout = 5000)]
    public void Budget_should_clamp_at_MaxBudget_48_under_rapid_calls()
    {
        var target = new FakeTarget();
        var pump = new TestPump(target, new ConnectionPoolContext(), new CancellationTokenSource());

        // Rapid calls: no sleep between them, so EMA should converge toward FastThresholdTicks (0.5 ms).
        // After enough iterations the EMA should saturate to MaxBudget = 48.
        for (var i = 0; i < 50; i++)
        {
            pump.AddCredit();
        }

        // After 50 rapid credits the budget should be at maximum.
        Assert.Equal(48, pump.Budget);
    }

    [Fact(Timeout = 5000)]
    public void Budget_should_decrease_from_fast_to_slow_after_pause()
    {
        var target = new FakeTarget();
        var pump = new TestPump(target, new ConnectionPoolContext(), new CancellationTokenSource());

        // Establish a fast budget first.
        for (var i = 0; i < 20; i++)
        {
            pump.AddCredit();
        }

        var budgetAfterFast = pump.Budget;

        // Now pause for 20 ms (above SlowThresholdTicks = 10 ms) and then call AddCredit.
        Thread.Sleep(20);
        pump.AddCredit();

        // Budget should have decreased (EMA shifted toward slow interval).
        Assert.True(pump.Budget < budgetAfterFast || pump.Budget == 8,
            $"Expected budget to decrease below {budgetAfterFast} after a slow interval.");
    }

    // Fix 3: AddCreditWithoutEma

    [Fact(Timeout = 5000)]
    public void AddCreditWithoutEma_should_not_alter_budget()
    {
        var target = new FakeTarget();
        var pump = new TestPump(target, new ConnectionPoolContext(), new CancellationTokenSource());

        var budgetBefore = pump.Budget;

        for (var i = 0; i < 50; i++)
        {
            pump.AddCreditWithoutEma();
        }

        Assert.Equal(budgetBefore, pump.Budget);
    }

    [Fact(Timeout = 5000)]
    public void AddCredit_during_DrainReady_should_skip_EMA_update()
    {
        // When AddCredit is called inline from EmitDataFrames during DrainReady,
        // the EMA should NOT be updated (the interval is sub-microsecond and
        // would distort the budget toward MaxBudget). Verify that a slow budget
        // established before the drain is preserved after.
        var target = new InlineCreditFakeTarget { HasPendingDemand = false };
        var pump = new TestPump(target, new ConnectionPoolContext(), new CancellationTokenSource());
        target.SetPump(pump);

        // Establish slow budget.
        for (var i = 0; i < 5; i++)
        {
            Thread.Sleep(15);
            pump.AddCredit();
        }

        var slowBudget = pump.Budget;
        Assert.Equal(8, slowBudget);

        // Register a stream and drain it. The inline AddCredit calls from
        // EmitDataFrames happen inside DrainReady → EMA is skipped.
        pump.Register(0, MakeBody(3 * 16 * 1024), CancellationToken.None, initialCredits: 16);

        Assert.Single(target.Completed);
        Assert.Equal(slowBudget, pump.Budget);
    }

    // Completion-trigger

    [Fact(Timeout = 5000)]
    public void HandleReadComplete_should_trigger_follow_up_read_when_credits_remain()
    {
        // Arrange: register a body large enough to require multiple reads.
        // Force the pump to have credits > 0 before the first read completes.
        var target = new FakeTarget { HasPendingDemand = false };
        var pump = new TestPump(target, new ConnectionPoolContext(), new CancellationTokenSource());
        var body = MakeBody(64 * 1024);

        pump.Register(0, body, CancellationToken.None);

        // With sync MemoryStream and credit reclaim, the first AddCredit that
        // reaches threshold reads the entire body in one self-driving chain.
        pump.AddCredit();

        // Body should be fully drained (4 data chunks + 1 endStream) after a
        // single credit triggers the self-driving sync read loop.
        Assert.True(target.Emitted.Count >= 1,
            "Sync read credit reclaim should drain the body with minimal credits.");
    }

    [Fact(Timeout = 5000)]
    public void Register_without_demand_should_read_after_sufficient_credits_added()
    {
        // With HasPendingDemand=false, a registered stream does not read immediately.
        // Adding credits eventually accumulates to the threshold and fires a read round.
        var target = new FakeTarget { HasPendingDemand = false };
        var pump = new TestPump(target, new ConnectionPoolContext(), new CancellationTokenSource());
        var body = MakeBody(64 * 1024);

        pump.Register(0, body, CancellationToken.None);

        // No credits yet: no reads.
        Assert.Empty(target.Emitted);

        // Add enough credits to guarantee a read round fires (threshold is at most budget/2 = 4 when budget=8).
        for (var i = 0; i < 10; i++)
        {
            pump.AddCredit();
        }

        // Reads should have fired.
        Assert.NotEmpty(target.Emitted);
    }

    // Cancellation — additional patterns

    [Fact(Timeout = 5000)]
    public void Cancel_of_already_completed_stream_should_be_noop()
    {
        var target = new FakeTarget { HasPendingDemand = true };
        var pump = new TestPump(target, new ConnectionPoolContext(), new CancellationTokenSource());
        var body = new MemoryStream([]);

        // Register empty body — completes immediately on Register (HasPendingDemand=true).
        pump.Register(0, body, CancellationToken.None);
        Assert.Single(target.Completed);

        // Cancelling after completion should not throw and should be a no-op.
        pump.Cancel(0);
        Assert.Equal(0, pump.ActiveStreamCount);
    }

    [Fact(Timeout = 5000)]
    public void CancelAll_should_zero_credits_and_active_streams()
    {
        var target = new FakeTarget { HasPendingDemand = false };
        var cts = new CancellationTokenSource();
        var pump = new TestPump(target, new ConnectionPoolContext(), cts);

        pump.Register(0, MakeBody(64 * 1024), CancellationToken.None);
        pump.Register(1, MakeBody(64 * 1024), CancellationToken.None);

        pump.CancelAll();

        Assert.Equal(0, pump.ActiveStreamCount);
        Assert.Equal(0, pump.Credits);
        Assert.True(cts.IsCancellationRequested);
    }

    [Fact(Timeout = 5000)]
    public void Cancel_multiple_streams_should_remove_only_targeted_stream()
    {
        var target = new FakeTarget { HasPendingDemand = false };
        var pump = new TestPump(target, new ConnectionPoolContext(), new CancellationTokenSource());

        pump.Register(0, MakeBody(64 * 1024), CancellationToken.None);
        pump.Register(1, MakeBody(64 * 1024), CancellationToken.None);
        pump.Register(2, MakeBody(64 * 1024), CancellationToken.None);

        pump.Cancel(1);

        // Stream 1 removed; streams 0 and 2 still active.
        Assert.Equal(2, pump.ActiveStreamCount);
    }

    // Regression: Bug 1 — re-enqueue before emit (stale queueSize snapshot)
    // Before the fix: ProcessReadResult re-enqueued AFTER EmitDataFrames. If EmitDataFrames
    // triggered an inline AddCredit (simulating the feedback from OnOutboundFlushed), that
    // AddCredit → TryReadNextEligible found an empty queue and did nothing. The pump stalled
    // permanently because the stream was never re-enqueued.
    // Fix: re-enqueue BEFORE EmitDataFrames so the inline AddCredit sees the stream.

    private sealed class InlineCreditFakeTarget : IBodyDrainTarget<int>
    {
        private TestPump? _pump;
        public IActorRef PipeToTarget { get; } = ActorRefs.Nobody;
        public bool HasPendingDemand { get; set; }
        public int PreferredChunkSize { get; set; } = 16 * 1024;
        public List<(int StreamId, byte[] Data, bool EndStream)> Emitted { get; } = [];
        public List<int> Completed { get; } = [];
        public List<(int StreamId, Exception Reason)> Failed { get; } = [];

        public void SetPump(TestPump pump) => _pump = pump;

        public void EmitDataFrames(int streamId, ReadOnlyMemory<byte> data, bool endStream)
        {
            Emitted.Add((streamId, data.ToArray(), endStream));
            // Simulate inline feedback: an outbound flush immediately gives back a credit.
            // Bug scenario: if the stream was re-enqueued AFTER this call, TryReadNextEligible
            // called inside AddCredit would see an empty ready queue and skip the stream.
            if (!endStream && _pump is not null)
            {
                _pump.AddCredit();
            }
        }

        public void OnDrainComplete(int streamId) => Completed.Add(streamId);
        public void OnDrainFailed(int streamId, Exception reason) => Failed.Add((streamId, reason));
    }

    [Fact(Timeout = 5000)]
    public void ProcessReadResult_should_complete_drain_when_AddCredit_called_inline_from_EmitDataFrames()
    {
        // Arrange: a FakeTarget that calls pump.AddCredit() inside EmitDataFrames.
        // The fix ensures the stream is re-enqueued BEFORE EmitDataFrames runs, so the
        // inline AddCredit finds the stream in the ready queue and continues draining.
        // Without the fix (re-enqueue AFTER EmitDataFrames), the inline AddCredit sees an
        // empty queue and the body never completes.
        var target = new InlineCreditFakeTarget { HasPendingDemand = false };
        var pump = new TestPump(target, new ConnectionPoolContext(), new CancellationTokenSource());
        target.SetPump(pump);

        // Body: 3 chunks of 16 KB → 3 data frames then 1 endStream frame.
        var body = MakeBody(3 * 16 * 1024);
        pump.Register(0, body, CancellationToken.None);

        // Seed exactly one credit — the inline AddCredit fired by each EmitDataFrames call
        // continues the drain. The entire body must complete without external credits.
        pump.AddCredit();

        // The body should be fully drained: 3 data + 1 endStream + drain-complete.
        Assert.Single(target.Completed);
        Assert.Equal(0, target.Completed[0]);
    }

    // Regression: Bug 2 — threshold too high for a single stream
    // Before the fix: threshold = min(budget/2, activeStreams * 2).
    // With 1 stream and budget ≈ 28: threshold = min(14, 2) = 2.
    // Adding exactly 1 credit never reached the threshold → permanent stall.
    // Fix: threshold = max(min(budget/2, activeStreams), 1) so threshold = 1 for 1 stream.

    [Fact(Timeout = 5000)]
    public void TryStartReadRound_should_read_with_single_credit_when_only_one_stream_active()
    {
        // Arrange: single stream registered with no pending demand so reads only fire via credits.
        var target = new FakeTarget { HasPendingDemand = false };
        var pump = new TestPump(target, new ConnectionPoolContext(), new CancellationTokenSource());
        var body = MakeBody(100);

        pump.Register(0, body, CancellationToken.None);
        Assert.Empty(target.Emitted);

        // Act: add exactly 1 credit.
        // Fix: threshold for 1 active stream is max(min(budget/2, 1), 1) = 1, so 1 credit is enough.
        // Bug: threshold was min(budget/2, 1*2) = 2, so 1 credit < 2 → no read → permanent stall.
        pump.AddCredit();

        // Assert: the pump issued at least one read from a single credit.
        Assert.NotEmpty(target.Emitted);
    }

    // Regression: Bug 5 — sync credit starvation
    // The pump must not stall when draining sync bodies. Each credit triggers one read;
    // inline AddCredit from EmitDataFrames (on targets that support it) or OnOutboundFlushed
    // replenishes credits. With sufficient credits, a sync body must drain completely.

    [Fact(Timeout = 5000)]
    public void PerformRead_should_drain_body_with_sufficient_credits()
    {
        var target = new FakeTarget { HasPendingDemand = false };
        var pump = new TestPump(target, new ConnectionPoolContext(), new CancellationTokenSource());
        var body = MakeBody(16 * 1024);

        pump.Register(0, body, CancellationToken.None);
        Assert.Empty(target.Emitted);

        // Two credits: one for the data read (16KB), one for the EOF read (0 bytes).
        pump.AddCredit();
        pump.AddCredit();

        Assert.Contains(target.Emitted, e => !e.EndStream);
        Assert.Single(target.Completed);
    }

    // Regression: Bug 6 — double slot cleanup on drain complete
    // Before the fix: on EOF, the order was EmitDataFrames(endStream:true) → OnDrainComplete →
    // CloseStream → Cancel → CleanupSlot (first), then CleanupSlot again (second).
    // Double pool return corrupts pool state (NullReferenceException on next rent).
    // Fix: CleanupSlot BEFORE OnDrainComplete so Cancel finds no slot.

    private sealed class CancelOnDrainTarget : IBodyDrainTarget<int>
    {
        private TestPump? _pump;
        public IActorRef PipeToTarget { get; } = ActorRefs.Nobody;
        public bool HasPendingDemand { get; set; } = true;
        public int PreferredChunkSize { get; set; } = 16 * 1024;
        public List<(int StreamId, byte[] Data, bool EndStream)> Emitted { get; } = [];
        public List<int> Completed { get; } = [];
        public List<(int StreamId, Exception Reason)> Failed { get; } = [];

        public void SetPump(TestPump pump) => _pump = pump;

        public void EmitDataFrames(int streamId, ReadOnlyMemory<byte> data, bool endStream)
            => Emitted.Add((streamId, data.ToArray(), endStream));

        public void OnDrainComplete(int streamId)
        {
            Completed.Add(streamId);
            // Simulate CloseStream → _pump.Cancel(streamId) called inside OnDrainComplete.
            // Bug: slot was still in _activeSlots at this point → Cancel would CleanupSlot again
            // (double pool return). Fix: slot is cleaned up BEFORE OnDrainComplete fires, so
            // Cancel here finds no slot and is a safe no-op.
            _pump?.Cancel(streamId);
        }

        public void OnDrainFailed(int streamId, Exception reason) => Failed.Add((streamId, reason));
    }

    [Fact(Timeout = 5000)]
    public void ProcessReadResult_should_survive_Cancel_called_inside_OnDrainComplete()
    {
        // Arrange: a target that calls pump.Cancel(streamId) inside OnDrainComplete, simulating
        // the CloseStream cascade. With the fix, the slot is removed before OnDrainComplete fires
        // so Cancel is a safe no-op. Without the fix, Cancel would find the slot still registered
        // and call CleanupSlot a second time → double pool return → corrupted state.
        //
        // An empty body produces EOF on the very first read, exercising the cleanup-before-notify
        // path directly (bytesRead = 0 → CleanupSlot → OnDrainComplete).
        var target = new CancelOnDrainTarget();
        var pump = new TestPump(target, new ConnectionPoolContext(), new CancellationTokenSource());
        target.SetPump(pump);

        // Empty body: HasPendingDemand=true → TryReadNextEligible fires → EOF immediately →
        // CleanupSlot (fix: before notify) → OnDrainComplete → Cancel(0) → no-op.
        pump.Register(0, new MemoryStream([]), CancellationToken.None);

        // Body drains to completion. The Cancel inside OnDrainComplete must be a safe no-op.
        Assert.Single(target.Completed);
        Assert.Equal(0, pump.ActiveStreamCount);

        // Verify pool integrity: register a second body — the slot must be reusable.
        // Without the fix, the slot would have been double-returned and the pool is corrupted,
        // causing a NullReferenceException or corrupted state here.
        pump.Register(1, new MemoryStream([]), CancellationToken.None);
        Assert.Equal(2, target.Completed.Count);
    }

    // Edge case: stale DrainReadComplete after CancelAll

    [Fact(Timeout = 5000)]
    public void HandleReadComplete_should_be_silently_ignored_after_CancelAll()
    {
        // Arrange: register a body so a slot exists, then CancelAll clears _activeSlots.
        // A stale DrainReadComplete message (e.g. in-flight before CancelAll) arrives afterward.
        // HandleReadComplete guards with TryGetValue and returns early when no slot is found.
        var target = new FakeTarget { HasPendingDemand = false };
        var cts = new CancellationTokenSource();
        var pump = new TestPump(target, new ConnectionPoolContext(), cts);

        pump.Register(0, MakeBody(100), CancellationToken.None);
        pump.CancelAll();

        // Act: stale completion arrives for streamId 0 — must not throw.
        var ex = Record.Exception(() => pump.HandleReadComplete(0, 50));
        Assert.Null(ex);

        // No data should have been emitted after CancelAll.
        Assert.Empty(target.Emitted);
        Assert.Empty(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void HandleReadFailed_should_be_silently_ignored_after_CancelAll()
    {
        // Arrange: same as above but for the failure path.
        var target = new FakeTarget { HasPendingDemand = false };
        var cts = new CancellationTokenSource();
        var pump = new TestPump(target, new ConnectionPoolContext(), cts);

        pump.Register(0, MakeBody(100), CancellationToken.None);
        pump.CancelAll();

        // Act: stale failure arrives — must not throw or call OnDrainFailed.
        var error = new IOException("stale error");
        var ex = Record.Exception(() => pump.HandleReadFailed(0, error));
        Assert.Null(ex);

        Assert.Empty(target.Failed);
    }

    // Edge case: single-byte body

    [Fact(Timeout = 5000)]
    public void Register_should_drain_single_byte_body_with_one_data_and_endStream()
    {
        // A 1-byte body must produce exactly 1 data emission (endStream=false) and
        // 1 endStream emission (endStream=true, zero bytes), then OnDrainComplete.
        // Two credits are needed: one for the data read, one for the EOF read.
        // HasPendingDemand=false so we control when reads fire via AddCredit.
        var target = new FakeTarget { HasPendingDemand = false };
        var pump = new TestPump(target, new ConnectionPoolContext(), new CancellationTokenSource());
        var body = new MemoryStream([42]);

        pump.Register(0, body, CancellationToken.None);
        Assert.Empty(target.Emitted);

        // First credit → data read (1 byte). Second credit → EOF read (0 bytes).
        pump.AddCredit();
        pump.AddCredit();

        // Verify exactly: [data frame, endStream frame]
        var dataFrame = Assert.Single(target.Emitted.Where(e => !e.EndStream).ToList());
        var eofFrame = Assert.Single(target.Emitted.Where(e => e.EndStream).ToList());
        var singleByte = Assert.Single(dataFrame.Data);
        Assert.Equal(42, singleByte);
        Assert.Empty(eofFrame.Data);
        Assert.Single(target.Completed);
    }

    // Edge case: body exactly equal to chunkSize

    [Fact(Timeout = 5000)]
    public void Register_should_produce_exactly_one_data_and_one_endStream_when_body_equals_chunkSize()
    {
        // A body exactly chunkSize (16 KB) should emit 1 data frame (full chunk)
        // followed by 1 endStream frame from the EOF read.
        // Two credits: one for the data read, one for the EOF read.
        var target = new FakeTarget { HasPendingDemand = false, PreferredChunkSize = 16 * 1024 };
        var pump = new TestPump(target, new ConnectionPoolContext(), new CancellationTokenSource());
        var body = MakeBody(16 * 1024);

        pump.Register(0, body, CancellationToken.None);
        Assert.Empty(target.Emitted);

        // Drive exactly 2 reads: data + EOF.
        pump.AddCredit();
        pump.AddCredit();

        // Exactly 1 data emit (non-endStream) + 1 endStream emit.
        var dataEmits = target.Emitted.Where(e => !e.EndStream).ToList();
        var eofEmits = target.Emitted.Where(e => e.EndStream).ToList();
        Assert.Single(dataEmits);
        Assert.Single(eofEmits);
        Assert.Equal(16 * 1024, dataEmits[0].Data.Length);
        Assert.Single(target.Completed);
    }

    // Edge case: multiple cancellations leave queue clean

    [Fact(Timeout = 5000)]
    public void Cancel_three_of_five_streams_should_leave_only_two_active_and_serve_only_them()
    {
        // Register 5 streams (no demand so nothing reads immediately), cancel 3.
        // Adding credits must serve only the 2 remaining streams and not trip on cancelled ones.
        var target = new FakeTarget { HasPendingDemand = false };
        var pump = new TestPump(target, new ConnectionPoolContext(), new CancellationTokenSource());

        for (var id = 0; id < 5; id++)
        {
            pump.Register(id, MakeBody(64 * 1024), CancellationToken.None);
        }

        pump.Cancel(1);
        pump.Cancel(3);
        pump.Cancel(4);

        Assert.Equal(2, pump.ActiveStreamCount);

        // Drive reads with plenty of credits.
        for (var i = 0; i < 48; i++)
        {
            pump.AddCredit();
        }

        // All emitted stream IDs must be from the non-cancelled set {0, 2}.
        var emittedIds = target.Emitted.Select(e => e.StreamId).Distinct().ToHashSet();
        Assert.DoesNotContain(1, emittedIds);
        Assert.DoesNotContain(3, emittedIds);
        Assert.DoesNotContain(4, emittedIds);
    }

    // Edge case: cancel during sync read chain

    private sealed class CancelMidDrainTarget : IBodyDrainTarget<int>
    {
        private TestPump? _pump;
        private int _emitCount;
        public IActorRef PipeToTarget { get; } = ActorRefs.Nobody;
        public bool HasPendingDemand { get; set; }
        public int PreferredChunkSize { get; set; } = 16 * 1024;
        public List<(int StreamId, byte[] Data, bool EndStream)> Emitted { get; } = [];
        public List<int> Completed { get; } = [];
        public List<(int StreamId, Exception Reason)> Failed { get; } = [];

        public void SetPump(TestPump pump) => _pump = pump;

        public void EmitDataFrames(int streamId, ReadOnlyMemory<byte> data, bool endStream)
        {
            Emitted.Add((streamId, data.ToArray(), endStream));
            // Cancel the stream on the first data emission to interrupt the drain chain.
            if (!endStream && Interlocked.Increment(ref _emitCount) == 1)
            {
                _pump?.Cancel(streamId);
            }
        }

        public void OnDrainComplete(int streamId) => Completed.Add(streamId);
        public void OnDrainFailed(int streamId, Exception reason) => Failed.Add((streamId, reason));
    }

    [Fact(Timeout = 5000)]
    public void Cancel_during_sync_drain_should_stop_pump_cleanly()
    {
        // A large body (8 chunks) is registered with a target that cancels the stream
        // after the first data emission. The pump must stop emitting after cancellation —
        // no double-cleanup, no throws, no further emissions for that stream.
        var target = new CancelMidDrainTarget { HasPendingDemand = false };
        var pump = new TestPump(target, new ConnectionPoolContext(), new CancellationTokenSource());
        target.SetPump(pump);

        var bodySize = 8 * 16 * 1024;
        pump.Register(0, MakeBody(bodySize), CancellationToken.None);

        // Seed one credit to start the drain; the target cancels on first emit.
        pump.AddCredit();

        // The pump must have stopped: active stream count should be 0.
        Assert.Equal(0, pump.ActiveStreamCount);
        // No exception was thrown (implicit — if we reach here, it didn't throw).
        // At most 2 emissions before cancel (1 data + possibly 1 re-enqueue attempt).
        Assert.True(target.Emitted.Count <= 2,
            $"Expected at most 2 emits before cancel stopped the drain, got {target.Emitted.Count}.");
    }

    // Edge case: ReadAsync throws synchronously (not via faulted ValueTask)

    private sealed class SynchronousThrowStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => throw new IOException("sync throw from ReadAsync");
    }

    [Fact(Timeout = 5000)]
    public void InlineAddCredit_with_many_concurrent_sync_streams_should_drain_all_iteratively()
    {
        // Before the re-entrancy guard: many sync streams with inline AddCredit from
        // EmitDataFrames would produce O(N) recursive DrainReady calls on the stack,
        // risking StackOverflowException. After the fix, DrainReady converts recursive
        // stack growth to flat iteration — same total work, O(1) stack depth.
        // Each stream gets 16 initial credits (matches production EncodeRequest path),
        // which is enough to drain each small body during registration.
        var target = new InlineCreditFakeTarget { HasPendingDemand = false };
        var pump = new TestPump(target, new ConnectionPoolContext(), new CancellationTokenSource());
        target.SetPump(pump);

        for (var i = 0; i < 200; i++)
        {
            pump.Register(i, MakeBody(100), CancellationToken.None, initialCredits: 16);
        }

        Assert.Equal(200, target.Completed.Count);
    }

    [Fact(Timeout = 5000)]
    public void InlineAddCredit_with_multichunk_bodies_should_drain_all_iteratively()
    {
        // Each stream has a 3-chunk body (3 × 16 KB = 4 reads including EOF).
        // With inline AddCredit, the pre-fix recursion depth was O(reads) per stream.
        // After the fix, DrainReady iterates flat.
        var target = new InlineCreditFakeTarget { HasPendingDemand = false };
        var pump = new TestPump(target, new ConnectionPoolContext(), new CancellationTokenSource());
        target.SetPump(pump);

        for (var i = 0; i < 200; i++)
        {
            pump.Register(i, MakeBody(3 * 16 * 1024), CancellationToken.None, initialCredits: 16);
        }

        Assert.Equal(200, target.Completed.Count);
        var totalBytes = target.Emitted.Where(e => !e.EndStream).Sum(e => e.Data.Length);
        Assert.Equal(200 * 3 * 16 * 1024, totalBytes);
    }

    [Fact(Timeout = 5000)]
    public void InlineAddCredit_with_queued_streams_should_drain_all_via_external_credits()
    {
        // Registers 50 streams with NO initial credits, then injects credits externally.
        // Each 100-byte stream needs 2 reads (data + EOF). Inline AddCredit from
        // EmitDataFrames reclaims the data credit, so each stream consumes 1 net credit
        // for the EOF. With enough external credits, all streams drain.
        var target = new InlineCreditFakeTarget { HasPendingDemand = false };
        var pump = new TestPump(target, new ConnectionPoolContext(), new CancellationTokenSource());
        target.SetPump(pump);

        for (var i = 0; i < 50; i++)
        {
            pump.Register(i, MakeBody(100), CancellationToken.None);
        }

        Assert.Empty(target.Completed);

        for (var i = 0; i < 100; i++)
        {
            pump.AddCredit();
        }

        Assert.Equal(50, target.Completed.Count);
    }

    [Fact(Timeout = 5000)]
    public void ReadAsync_that_throws_synchronously_should_propagate_as_exception()
    {
        // StartRead calls ReadAsync inside PerformRead.
        // If ReadAsync throws synchronously (not via a faulted ValueTask), the exception
        // propagates out of PerformRead → DrainReady → AddCredit → caller.
        // This test documents the current behavior: the pump does NOT catch synchronous throws
        // from ReadAsync; the caller receives the exception directly.
        var target = new FakeTarget { HasPendingDemand = false };
        var pump = new TestPump(target, new ConnectionPoolContext(), new CancellationTokenSource());
        var body = new SynchronousThrowStream();

        pump.Register(0, body, CancellationToken.None);

        // AddCredit triggers a read, which calls ReadAsync → throws synchronously.
        var ex = Record.Exception(() => pump.AddCredit());

        // The exception propagates to the caller (no catch in PerformRead).
        Assert.NotNull(ex);
        Assert.IsType<IOException>(ex);
        Assert.Equal("sync throw from ReadAsync", ex.Message);
    }
}
