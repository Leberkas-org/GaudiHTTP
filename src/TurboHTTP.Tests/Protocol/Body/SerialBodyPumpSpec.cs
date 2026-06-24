using System.Buffers;
using Akka.Actor;
using TurboHTTP.Protocol.Body;

namespace TurboHTTP.Tests.Protocol.Body;

public sealed class SerialBodyPumpSpec
{
    private sealed class FakeTarget : IBodyDrainTarget<int>
    {
        public List<(int StreamId, byte[] Data, bool EndStream)> Emitted { get; } = [];
        public List<int> Completed { get; } = [];
        public List<(int StreamId, Exception Reason)> Failed { get; } = [];
        public IActorRef PipeToTarget { get; } = ActorRefs.Nobody;
        public bool HasPendingDemand => false;
        public int PreferredChunkSize => 16 * 1024;

        public void EmitDataFrames(int streamId, ReadOnlyMemory<byte> data, bool endStream)
        {
            Emitted.Add((streamId, data.ToArray(), endStream));
        }

        public void OnDrainComplete(int streamId) => Completed.Add(streamId);
        public void OnDrainFailed(int streamId, Exception reason) => Failed.Add((streamId, reason));
    }

    /// <summary>
    /// Target that calls OnCapacityAvailable() synchronously after EmitDataFrames,
    /// simulating H1.0 behavior where the target drives the pump inline.
    /// </summary>
    private sealed class AutoResumeTarget : IBodyDrainTarget<int>
    {
        private SerialBodyPump? _pump;
        public List<(int StreamId, byte[] Data, bool EndStream)> Emitted { get; } = [];
        public List<int> Completed { get; } = [];
        public List<(int StreamId, Exception Reason)> Failed { get; } = [];
        public IActorRef PipeToTarget { get; } = ActorRefs.Nobody;
        public bool HasPendingDemand => false;
        public int PreferredChunkSize => 16 * 1024;

        public void SetPump(SerialBodyPump pump) => _pump = pump;

        public void EmitDataFrames(int streamId, ReadOnlyMemory<byte> data, bool endStream)
        {
            Emitted.Add((streamId, data.ToArray(), endStream));
            if (!endStream)
            {
                _pump?.OnCapacityAvailable();
            }
        }

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
    public void Register_should_emit_body_immediately_for_sync_stream()
    {
        // maxCapacity=2: allows 2 reads in flight. A 100-byte body with 1024-byte chunk
        // produces 1 data read (100 bytes) + 1 EOF read (0 bytes) = exactly 2 reads.
        var target = new FakeTarget();
        var pump = new SerialBodyPump(target, new CancellationTokenSource(), chunkSize: 1024, maxCapacity: 2);
        var body = MakeBody(100);

        pump.Register(body, 100, CancellationToken.None);

        Assert.Equal(2, target.Emitted.Count);
        Assert.Equal(100, target.Emitted[0].Data.Length);
        Assert.False(target.Emitted[0].EndStream);
        Assert.Empty(target.Emitted[1].Data);
        Assert.True(target.Emitted[1].EndStream);
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void Register_should_emit_endStream_on_empty_body()
    {
        var target = new FakeTarget();
        var pump = new SerialBodyPump(target, new CancellationTokenSource(), chunkSize: 1024, maxCapacity: 2);
        var body = new MemoryStream([]);

        pump.Register(body, 0, CancellationToken.None);

        Assert.Single(target.Emitted);
        Assert.True(target.Emitted[0].EndStream);
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void Register_should_chunk_large_body_with_auto_resume()
    {
        // Large body with small chunks and maxCapacity=1 — needs AutoResumeTarget
        // to call OnCapacityAvailable inline (H1.0 convention).
        var target = new AutoResumeTarget();
        var pump = new SerialBodyPump(target, new CancellationTokenSource(), chunkSize: 64, maxCapacity: 1);
        target.SetPump(pump);
        var body = MakeBody(200);

        pump.Register(body, 200, CancellationToken.None);

        var dataEmits = target.Emitted.Where(e => !e.EndStream).ToList();
        Assert.True(dataEmits.Count >= 3);
        Assert.Equal(200, dataEmits.Sum(e => e.Data.Length));
        Assert.True(target.Emitted[^1].EndStream);
    }

    [Fact(Timeout = 5000)]
    public void OnCapacityAvailable_should_resume_after_capacity_exhaustion()
    {
        var target = new FakeTarget();
        var pump = new SerialBodyPump(target, new CancellationTokenSource(), chunkSize: 64, maxCapacity: 1);
        var body = MakeBody(200);

        pump.Register(body, 200, CancellationToken.None);

        // maxCapacity=1: pump emits 1 chunk then stops (FakeTarget doesn't auto-resume)
        Assert.Single(target.Emitted, e => !e.EndStream);
        Assert.Equal(64, target.Emitted[0].Data.Length);

        // Manual resume
        pump.OnCapacityAvailable();
        Assert.Equal(2, target.Emitted.Count(e => !e.EndStream));

        // Resume until complete
        while (target.Completed.Count == 0)
        {
            pump.OnCapacityAvailable();
        }

        Assert.Equal(200, target.Emitted.Where(e => !e.EndStream).Sum(e => e.Data.Length));
    }

    [Fact(Timeout = 5000)]
    public void Cancel_should_stop_drain()
    {
        var target = new FakeTarget();
        var pump = new SerialBodyPump(target, new CancellationTokenSource(), chunkSize: 1024, maxCapacity: 2);
        var body = MakeBody(100);

        pump.Register(body, 100, CancellationToken.None);
        // Already completed since MemoryStream is sync with maxCapacity=2
        Assert.Single(target.Completed);

        // Cancel after complete should be no-op
        pump.Cancel();
    }

    [Fact(Timeout = 5000)]
    public void Cancel_midDrain_should_not_call_onDrainComplete()
    {
        var target = new FakeTarget();
        var pump = new SerialBodyPump(target, new CancellationTokenSource(), chunkSize: 64, maxCapacity: 1);
        var body = MakeBody(200);

        pump.Register(body, 200, CancellationToken.None);
        // maxCapacity=1, FakeTarget: 1 chunk emitted, pump paused
        Assert.Single(target.Emitted, e => !e.EndStream);
        Assert.Empty(target.Completed);

        pump.Cancel();

        // Cancel should NOT fire OnDrainComplete
        Assert.Empty(target.Completed);
        Assert.Empty(target.Failed);
    }

    [Fact(Timeout = 5000)]
    public void Cleanup_should_be_idempotent()
    {
        var target = new FakeTarget();
        var pump = new SerialBodyPump(target, new CancellationTokenSource(), chunkSize: 1024, maxCapacity: 2);

        pump.Cleanup();
        pump.Cleanup();
    }

    [Fact(Timeout = 5000)]
    public void Capacity2_should_drain_two_chunk_body_without_resume()
    {
        // maxCapacity=2 allows 2 reads before pausing. A body that fits in 2 reads
        // (1 data read + 1 EOF read) completes without OnCapacityAvailable.
        var target = new FakeTarget();
        var pump = new SerialBodyPump(target, new CancellationTokenSource(), chunkSize: 64, maxCapacity: 2);
        var body = MakeBody(64);

        pump.Register(body, 64, CancellationToken.None);

        Assert.Equal(64, target.Emitted.Where(e => !e.EndStream).Sum(e => e.Data.Length));
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void Capacity2_should_need_resume_for_large_body()
    {
        // maxCapacity=2: pump issues 2 reads, pauses, needs OnCapacityAvailable to continue.
        var target = new FakeTarget();
        var pump = new SerialBodyPump(target, new CancellationTokenSource(), chunkSize: 64, maxCapacity: 2);
        var body = MakeBody(200);

        pump.Register(body, 200, CancellationToken.None);

        // 2 reads issued: 64 + 64 = 128 bytes emitted
        Assert.Equal(128, target.Emitted.Where(e => !e.EndStream).Sum(e => e.Data.Length));
        Assert.Empty(target.Completed);

        // Resume until complete
        while (target.Completed.Count == 0)
        {
            pump.OnCapacityAvailable();
        }

        Assert.Equal(200, target.Emitted.Where(e => !e.EndStream).Sum(e => e.Data.Length));
    }

    [Fact(Timeout = 5000)]
    public void Sync_reads_should_complete_without_starvation_guard()
    {
        // Starvation guard removed — pump should drain all chunks without yielding.
        // Use AutoResumeTarget so the pump loops without manual OnCapacityAvailable calls.
        // maxCapacity=1 + auto-resume = pump reads one chunk, emits, auto-resumes, reads next...
        // Without the guard, it drains all 65 chunks synchronously.
        var target = new AutoResumeTarget();
        var pump = new SerialBodyPump(target, new CancellationTokenSource(), chunkSize: 16, maxCapacity: 1);
        target.SetPump(pump);
        var body = MakeBody(65 * 16);

        pump.Register(body, 65 * 16, CancellationToken.None);

        // All 65 data chunks emitted + EOF (no starvation guard to interrupt)
        var emittedBytes = target.Emitted.Where(e => !e.EndStream).Sum(e => e.Data.Length);
        Assert.Equal(65 * 16, emittedBytes);
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void CtsDisposal_should_happen_on_complete()
    {
        var target = new FakeTarget();
        var connCts = new CancellationTokenSource();
        var reqCts = new CancellationTokenSource();
        var pump = new SerialBodyPump(target, connCts, chunkSize: 1024, maxCapacity: 2);

        pump.Register(MakeBody(100), 100, reqCts.Token);

        Assert.Single(target.Completed);
        // Verify no exception when disposing reqCts (linked CTS should already be disposed by pump)
        reqCts.Dispose();
    }
}
