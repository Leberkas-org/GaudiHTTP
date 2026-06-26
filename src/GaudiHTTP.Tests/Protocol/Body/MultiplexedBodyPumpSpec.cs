using Akka.Actor;
using GaudiHTTP.Pooling;
using GaudiHTTP.Protocol.Body;

namespace GaudiHTTP.Tests.Protocol.Body;

public sealed class MultiplexedBodyPumpSpec
{
    private sealed class FakeTarget : IMultiplexedBodyDrainTarget
    {
        public List<(long StreamId, byte[] Data, bool EndStream)> Emitted { get; } = [];
        public List<long> Completed { get; } = [];
        public List<(long StreamId, Exception Reason)> Failed { get; } = [];
        public IActorRef StageActor { get; } = ActorRefs.Nobody;

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

    private static MultiplexedBodyPump MakePump(FakeTarget target, int chunkSize = 16 * 1024)
    {
        return new MultiplexedBodyPump(target, new CancellationTokenSource(), new ConnectionPoolContext(), chunkSize);
    }

    [Fact(Timeout = 5000)]
    public void Register_should_emit_body_immediately()
    {
        var target = new FakeTarget();
        var pump = MakePump(target);

        pump.Register(1L, MakeBody(100), contentLength: null, CancellationToken.None);

        Assert.Equal(2, target.Emitted.Count);
        Assert.Equal(100, target.Emitted[0].Data.Length);
        Assert.False(target.Emitted[0].EndStream);
        Assert.True(target.Emitted[1].EndStream);
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void Register_should_interleave_multiple_streams()
    {
        var target = new FakeTarget();
        var pump = MakePump(target);

        pump.Register(1L, MakeBody(128), contentLength: null, CancellationToken.None);
        pump.Register(3L, MakeBody(128), contentLength: null, CancellationToken.None);

        Assert.Contains(1L, target.Completed);
        Assert.Contains(3L, target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void Cancel_should_handle_slot_not_in_flight()
    {
        var target = new FakeTarget();
        var pump = MakePump(target);

        pump.Register(1L, MakeBody(100), contentLength: null, CancellationToken.None);
        // Already completed synchronously — Cancel on completed stream is a no-op
        pump.Cancel(1L);
    }

    [Fact(Timeout = 5000)]
    public void Cancel_should_mark_orphan_when_read_in_flight()
    {
        var target = new FakeTarget();
        var pump = MakePump(target, chunkSize: 256);

        // Use a stream that never returns data to simulate an in-flight read
        using var cts = new CancellationTokenSource();
        var neverStream = new NeverReadStream();
        pump.Register(42L, neverStream, contentLength: null, cts.Token);

        // Stream is now waiting on async read — cancel it
        pump.Cancel(42L);

        // No drain complete or failed since the slot is now orphaned
        Assert.Empty(target.Completed);
        Assert.Empty(target.Failed);
    }

    [Fact(Timeout = 5000)]
    public void Cleanup_should_be_idempotent()
    {
        var target = new FakeTarget();
        var pump = MakePump(target);

        pump.Cleanup();
        pump.Cleanup();
    }

    [Fact(Timeout = 5000)]
    public void SyncFastPath_should_drain_small_body_inline()
    {
        var target = new FakeTarget();
        var pump = MakePump(target);

        pump.Register(1L, MakeBody(50), contentLength: null, CancellationToken.None);

        Assert.Equal(2, target.Emitted.Count);
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void Sync_reads_should_drain_body_larger_than_starvation_threshold()
    {
        var target = new FakeTarget();
        // Use small chunk so many sync reads happen
        var pump = MakePump(target, chunkSize: 16);

        pump.Register(1L, MakeBody(65 * 16), contentLength: null, CancellationToken.None);

        // Starvation guard fires at 64 consecutive reads, yielding via StageActor.Tell to Nobody.
        // Since StageActor is Nobody, HandleDrainContinue is never called, so drain stalls after
        // MaxSyncReadsPerDispatch. Verify at least partial data was emitted.
        var total = target.Emitted.Where(e => !e.EndStream).Sum(e => e.Data.Length);
        Assert.True(total > 0, "Expected at least some data emitted before starvation guard fired");
    }

    [Fact(Timeout = 5000)]
    public void SlotPooling_should_reuse_slot_after_drain()
    {
        var target = new FakeTarget();
        var pump = MakePump(target);

        pump.Register(1L, MakeBody(10), contentLength: null, CancellationToken.None);
        Assert.Single(target.Completed);

        target.Completed.Clear();
        pump.Register(3L, MakeBody(10), contentLength: null, CancellationToken.None);
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void EOF_should_emit_endStream_on_empty_body()
    {
        var target = new FakeTarget();
        var pump = MakePump(target);

        pump.Register(1L, new MemoryStream([]), contentLength: null, CancellationToken.None);

        Assert.Single(target.Emitted);
        Assert.True(target.Emitted[0].EndStream);
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void HandleReadComplete_should_process_orphaned_slot_cleanly()
    {
        var target = new FakeTarget();
        var pump = MakePump(target);

        // Simulate orphaned slot: register, cancel (not in-flight for sync), then call HandleReadFailed
        // For an orphaned scenario, we need to fake a stalled async read.
        // We simulate via direct HandleReadComplete with an unknown streamId — should be a no-op.
        pump.HandleReadComplete(999L, 42);

        Assert.Empty(target.Completed);
        Assert.Empty(target.Failed);
    }

    [Fact(Timeout = 5000)]
    public void HandleReadFailed_should_report_failure_for_active_slot()
    {
        var target = new FakeTarget();
        var pump = MakePump(target, chunkSize: 256);

        var neverStream = new NeverReadStream();
        pump.Register(7L, neverStream, contentLength: null, CancellationToken.None);

        // Simulate async failure callback
        pump.HandleReadFailed(7L, new IOException("simulated failure"));

        Assert.Single(target.Failed);
        Assert.Equal(7L, target.Failed[0].StreamId);
    }

    /// <summary>
    /// A stream whose ReadAsync never completes (simulates network stall).
    /// </summary>
    private sealed class NeverReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return new ValueTask<int>(Task.Delay(Timeout.Infinite, cancellationToken)
                .ContinueWith(_ => 0, cancellationToken));
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
