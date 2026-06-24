using Akka.Actor;
using TurboHTTP.Pooling;
using TurboHTTP.Protocol.Body;

namespace TurboHTTP.Tests.Protocol.Body;

public sealed class MultiplexedBodyPumpSpec
{
    private sealed class FakeTarget : IBodyDrainTarget<long>
    {
        public List<(long StreamId, byte[] Data, bool EndStream)> Emitted { get; } = [];
        public List<long> Completed { get; } = [];
        public List<(long StreamId, Exception Reason)> Failed { get; } = [];
        public IActorRef PipeToTarget { get; } = ActorRefs.Nobody;
        public bool HasPendingDemand => false;
        public int PreferredChunkSize => 16 * 1024;

        public void EmitDataFrames(long streamId, ReadOnlyMemory<byte> data, bool endStream)
        {
            Emitted.Add((streamId, data.ToArray(), endStream));
        }

        public void OnDrainComplete(long streamId) => Completed.Add(streamId);
        public void OnDrainFailed(long streamId, Exception reason) => Failed.Add((streamId, reason));
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
    public void Register_should_emit_body_immediately()
    {
        var target = new FakeTarget();
        var pump = new MultiplexedBodyPump(target, new ConnectionPoolContext(), new CancellationTokenSource());

        pump.Register(1L, MakeBody(100), 100, CancellationToken.None);

        Assert.Equal(2, target.Emitted.Count);
        Assert.Equal(100, target.Emitted[0].Data.Length);
        Assert.True(target.Emitted[1].EndStream);
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void RoundRobin_should_interleave_streams()
    {
        var target = new FakeTarget();
        var pump = new MultiplexedBodyPump(target, new ConnectionPoolContext(), new CancellationTokenSource());

        pump.Register(1L, MakeBody(128), 128, CancellationToken.None);
        pump.Register(3L, MakeBody(128), 128, CancellationToken.None);

        Assert.Contains(1L, target.Completed);
        Assert.Contains(3L, target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void Cancel_should_handle_orphan()
    {
        var target = new FakeTarget();
        var pump = new MultiplexedBodyPump(target, new ConnectionPoolContext(), new CancellationTokenSource());

        pump.Register(1L, MakeBody(100), 100, CancellationToken.None);
        pump.Cancel(1L);
    }

    [Fact(Timeout = 5000)]
    public void Cleanup_should_be_idempotent()
    {
        var target = new FakeTarget();
        var pump = new MultiplexedBodyPump(target, new ConnectionPoolContext(), new CancellationTokenSource());

        pump.Cleanup();
        pump.Cleanup();
    }

    [Fact(Timeout = 5000)]
    public void SyncFastPath_should_drain_inline()
    {
        var target = new FakeTarget();
        var pump = new MultiplexedBodyPump(target, new ConnectionPoolContext(), new CancellationTokenSource());

        pump.Register(1L, MakeBody(50), 50, CancellationToken.None);

        Assert.Equal(2, target.Emitted.Count);
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void Sync_reads_should_complete_without_starvation_guard()
    {
        var target = new FakeTarget();
        var pump = new MultiplexedBodyPump(target, new ConnectionPoolContext(), new CancellationTokenSource());

        pump.Register(1L, MakeBody(65 * 16), 65 * 16, CancellationToken.None);

        // All chunks emitted + EOF
        var total = target.Emitted.Where(e => !e.EndStream).Sum(e => e.Data.Length);
        Assert.Equal(65 * 16, total);
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void SlotPooling_should_reuse_after_drain()
    {
        var target = new FakeTarget();
        var pump = new MultiplexedBodyPump(target, new ConnectionPoolContext(), new CancellationTokenSource());

        pump.Register(1L, MakeBody(10), 10, CancellationToken.None);
        Assert.Single(target.Completed);

        target.Completed.Clear();
        pump.Register(3L, MakeBody(10), 10, CancellationToken.None);
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void EOF_should_emit_endStream()
    {
        var target = new FakeTarget();
        var pump = new MultiplexedBodyPump(target, new ConnectionPoolContext(), new CancellationTokenSource());

        pump.Register(1L, new MemoryStream([]), 0, CancellationToken.None);

        Assert.Single(target.Emitted);
        Assert.True(target.Emitted[0].EndStream);
    }
}
