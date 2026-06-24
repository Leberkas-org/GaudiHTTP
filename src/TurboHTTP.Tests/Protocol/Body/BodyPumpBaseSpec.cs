using Akka.Actor;
using TurboHTTP.Pooling;
using TurboHTTP.Protocol.Body;

namespace TurboHTTP.Tests.Protocol.Body;

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

        pump.Register(0, body, 100, CancellationToken.None);

        Assert.True(target.Emitted.Count >= 1);
    }

    [Fact(Timeout = 5000)]
    public void Register_should_not_read_without_demand_or_credits()
    {
        var target = new FakeTarget { HasPendingDemand = false };
        var pump = new TestPump(target, new ConnectionPoolContext(), new CancellationTokenSource());
        var body = MakeBody(100);

        pump.Register(0, body, 100, CancellationToken.None);

        Assert.Empty(target.Emitted);
    }

    [Fact(Timeout = 5000)]
    public void AddCredit_should_trigger_read_round_when_threshold_reached()
    {
        var target = new FakeTarget { HasPendingDemand = false };
        var pump = new TestPump(target, new ConnectionPoolContext(), new CancellationTokenSource());
        var body = MakeBody(100);

        pump.Register(0, body, 100, CancellationToken.None);
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

        pump.Register(0, body, 100, CancellationToken.None);

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

        pump.Register(0, body, 100, CancellationToken.None);
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

        pump.Register(0, body, 100, CancellationToken.None);
        pump.Cancel(0);

        Assert.Equal(0, pump.ActiveStreamCount);
    }

    [Fact(Timeout = 5000)]
    public void HandleReadComplete_should_emit_endStream_on_empty_body()
    {
        var target = new FakeTarget { HasPendingDemand = true };
        var pump = new TestPump(target, new ConnectionPoolContext(), new CancellationTokenSource());
        var body = new MemoryStream([]);

        pump.Register(0, body, 0, CancellationToken.None);

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

        pump.Register(0, body, 100, CancellationToken.None);
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

        pump.Register(0, body0, 64 * 1024, CancellationToken.None);
        pump.Register(1, body1, 64 * 1024, CancellationToken.None);

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
        pump.Register(0, MakeBody(bodySize), bodySize, CancellationToken.None);
        pump.Register(1, MakeBody(bodySize), bodySize, CancellationToken.None);
        pump.Register(2, MakeBody(bodySize), bodySize, CancellationToken.None);

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

    // Completion-trigger

    [Fact(Timeout = 5000)]
    public void HandleReadComplete_should_trigger_follow_up_read_when_credits_remain()
    {
        // Arrange: register a body large enough to require multiple reads.
        // Force the pump to have credits > 0 before the first read completes.
        var target = new FakeTarget { HasPendingDemand = false };
        var pump = new TestPump(target, new ConnectionPoolContext(), new CancellationTokenSource());
        var body = MakeBody(64 * 1024);

        pump.Register(0, body, 64 * 1024, CancellationToken.None);

        // Add credits to kick off a read round (threshold ~2 for single stream).
        pump.AddCredit();
        pump.AddCredit();
        pump.AddCredit();

        var emittedAfterFirstRound = target.Emitted.Count;

        // Add more credits while the stream still has data — should trigger further reads.
        pump.AddCredit();
        pump.AddCredit();

        // Each successive AddCredit with remaining data should produce more reads.
        Assert.True(target.Emitted.Count > emittedAfterFirstRound,
            "Follow-up reads should fire when credits are available after a read completes.");
    }

    [Fact(Timeout = 5000)]
    public void Register_without_demand_should_read_after_sufficient_credits_added()
    {
        // With HasPendingDemand=false, a registered stream does not read immediately.
        // Adding credits eventually accumulates to the threshold and fires a read round.
        var target = new FakeTarget { HasPendingDemand = false };
        var pump = new TestPump(target, new ConnectionPoolContext(), new CancellationTokenSource());
        var body = MakeBody(64 * 1024);

        pump.Register(0, body, 64 * 1024, CancellationToken.None);

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
        pump.Register(0, body, 0, CancellationToken.None);
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

        pump.Register(0, MakeBody(64 * 1024), 64 * 1024, CancellationToken.None);
        pump.Register(1, MakeBody(64 * 1024), 64 * 1024, CancellationToken.None);

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

        pump.Register(0, MakeBody(64 * 1024), 64 * 1024, CancellationToken.None);
        pump.Register(1, MakeBody(64 * 1024), 64 * 1024, CancellationToken.None);
        pump.Register(2, MakeBody(64 * 1024), 64 * 1024, CancellationToken.None);

        pump.Cancel(1);

        // Stream 1 removed; streams 0 and 2 still active.
        Assert.Equal(2, pump.ActiveStreamCount);
    }
}
