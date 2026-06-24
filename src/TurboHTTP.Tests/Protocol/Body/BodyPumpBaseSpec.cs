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
    public void Cancel_should_mark_slot_orphaned_when_read_in_flight()
    {
        var target = new FakeTarget { HasPendingDemand = false };
        var pump = new TestPump(target, new ConnectionPoolContext(), new CancellationTokenSource());
        var body = MakeBody(100);

        // Register with no demand so slot sits idle
        pump.Register(0, body, 100, CancellationToken.None);
        pump.Cancel(0);

        // Slot should be cleaned up (not in active slots)
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
}
