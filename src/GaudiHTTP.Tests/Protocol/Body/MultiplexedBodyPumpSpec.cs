using Akka.Actor;
using GaudiHTTP.Protocol.Body;

namespace GaudiHTTP.Tests.Protocol.Body;

public sealed class MultiplexedBodyPumpSpec
{
    private const int BigCredit = 1 * 1024 * 1024;

    private sealed class FakeTarget : IMultiplexedBodyDrainTarget
    {
        public List<(long StreamId, byte[] Data, bool EndStream)> Emitted { get; } = [];
        public List<long> Completed { get; } = [];
        public List<(long StreamId, Exception Reason)> Failed { get; } = [];
        public List<object> PendingMessages { get; } = [];
        public IActorRef StageActor => _interceptor;
        private readonly MessageInterceptor _interceptor;

        public FakeTarget()
        {
            _interceptor = new MessageInterceptor(PendingMessages);
        }

        public void EmitDataFrames(long streamId, ReadOnlyMemory<byte> data, bool endStream)
        {
            Emitted.Add((streamId, data.ToArray(), endStream));
        }

        public void OnDrainComplete(long streamId) => Completed.Add(streamId);
        public void OnDrainFailed(long streamId, Exception reason) => Failed.Add((streamId, reason));

        public int EmittedBytes(long streamId) =>
            Emitted.Where(e => e.StreamId == streamId && !e.EndStream).Sum(e => e.Data.Length);

        public bool EndStreamSeen(long streamId) =>
            Emitted.Any(e => e.StreamId == streamId && e.EndStream);
    }

    private sealed class MessageInterceptor : MinimalActorRef
    {
        private readonly List<object> _messages;
        public MessageInterceptor(List<object> messages) => _messages = messages;
        public override ActorPath Path { get; } = new RootActorPath(new Address("akka", "test")) / "fake-mux";
        public override IActorRefProvider Provider => throw new NotSupportedException();
        protected override void TellInternal(object message, IActorRef sender) => _messages.Add(message);
    }

    /// <summary>
    /// Drives read completions while topping up per-stream credit for the given streams each turn,
    /// so a healthy drain never stalls on budget. A stream NOT listed is starved and parks once its
    /// initial per-stream budget is exhausted.
    /// </summary>
    private static void DrainToCompletion(
        MultiplexedBodyPump pump,
        FakeTarget target,
        long[] creditStreams,
        int expectedCompletions = 1,
        int maxIterations = 10_000)
    {
        var iterations = 0;
        while (target.Completed.Count < expectedCompletions
               && target.Failed.Count == 0
               && iterations++ < maxIterations)
        {
            foreach (var s in creditStreams)
            {
                pump.OnCapacityAvailable(s, BigCredit);
            }

            if (target.PendingMessages.Count == 0)
            {
                break;
            }

            var msg = target.PendingMessages[0];
            target.PendingMessages.RemoveAt(0);
            switch (msg)
            {
                case BodyReadComplete<long> rc:
                    pump.HandleReadComplete(rc.StreamId, rc.BytesRead);
                    break;
            }
        }
    }

    /// <summary>
    /// Drives read completions WITHOUT ever granting outbound capacity, simulating a transport
    /// that has stopped draining. A backpressured pump must stall after emitting at most
    /// <c>maxBytesPerStream</c> bytes rather than reading the whole body into outbound buffers.
    /// </summary>
    private static void DriveReadsWithoutCapacity(
        MultiplexedBodyPump pump,
        FakeTarget target,
        int maxIterations = 10_000)
    {
        var iterations = 0;
        while (target.PendingMessages.Count > 0 && iterations++ < maxIterations)
        {
            var msg = target.PendingMessages[0];
            target.PendingMessages.RemoveAt(0);
            switch (msg)
            {
                case BodyReadComplete<long> rc:
                    pump.HandleReadComplete(rc.StreamId, rc.BytesRead);
                    break;
            }
        }
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

    private static MultiplexedBodyPump MakePump(
        FakeTarget target, int chunkSize = 16 * 1024, int maxBytesPerStream = 4 * 1024 * 1024)
    {
        return new MultiplexedBodyPump(target, new CancellationTokenSource(), chunkSize, maxBytesPerStream);
    }

    [Fact(Timeout = 5000)]
    public void Register_should_emit_body_immediately()
    {
        var target = new FakeTarget();
        var pump = MakePump(target);

        pump.Register(1L, MakeBody(100), contentLength: null, CancellationToken.None);
        DrainToCompletion(pump, target, [1L]);

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
        DrainToCompletion(pump, target, [1L, 3L], expectedCompletions: 2);

        Assert.Contains(1L, target.Completed);
        Assert.Contains(3L, target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void Cancel_should_handle_slot_not_in_flight()
    {
        var target = new FakeTarget();
        var pump = MakePump(target);

        pump.Register(1L, MakeBody(100), contentLength: null, CancellationToken.None);
        DrainToCompletion(pump, target, [1L]);
        // Drain completed synchronously — Cancel on completed stream is a no-op
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
        DrainToCompletion(pump, target, [1L]);

        Assert.Equal(2, target.Emitted.Count);
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void Many_consecutive_sync_reads_should_drain_fully()
    {
        var target = new FakeTarget();
        // Small chunk so the body needs dozens of consecutive sync reads to drain.
        var pump = MakePump(target, chunkSize: 16);

        pump.Register(1L, MakeBody(65 * 16), contentLength: null, CancellationToken.None);
        DrainToCompletion(pump, target, [1L]);

        var total = target.Emitted.Where(e => !e.EndStream).Sum(e => e.Data.Length);
        Assert.Equal(65 * 16, total);
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void SlotPooling_should_reuse_slot_after_drain()
    {
        var target = new FakeTarget();
        var pump = MakePump(target);

        pump.Register(1L, MakeBody(10), contentLength: null, CancellationToken.None);
        DrainToCompletion(pump, target, [1L]);
        Assert.Single(target.Completed);

        target.Completed.Clear();
        pump.Register(3L, MakeBody(10), contentLength: null, CancellationToken.None);
        DrainToCompletion(pump, target, [3L]);
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void EOF_should_emit_endStream_on_empty_body()
    {
        var target = new FakeTarget();
        var pump = MakePump(target);

        pump.Register(1L, new MemoryStream([]), contentLength: null, CancellationToken.None);
        DrainToCompletion(pump, target, [1L]);

        Assert.Single(target.Emitted);
        Assert.True(target.Emitted[0].EndStream);
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void HandleReadComplete_should_process_orphaned_slot_cleanly()
    {
        var target = new FakeTarget();
        var pump = MakePump(target);

        // Direct HandleReadComplete with an unknown streamId — should be a no-op.
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

    [Fact(Timeout = 5000)]
    public void Pump_should_bound_in_flight_emissions_to_per_stream_budget_without_drain_signal()
    {
        var target = new FakeTarget();
        // 40 chunks of body, but only 4 chunks worth of per-stream budget and no transport drain:
        // the stream must park after emitting at most its budget.
        var pump = MakePump(target, chunkSize: 16, maxBytesPerStream: 4 * 16);

        pump.Register(1L, MakeBody(16 * 40), contentLength: null, CancellationToken.None);
        DriveReadsWithoutCapacity(pump, target);

        var emittedBytes = target.EmittedBytes(1L);
        Assert.True(
            emittedBytes <= 4 * 16,
            $"expected at most {4 * 16} in-flight bytes without a drain signal, got {emittedBytes}");
        Assert.Empty(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void OnCapacityAvailable_should_resume_and_complete_drain()
    {
        var target = new FakeTarget();
        var pump = MakePump(target, chunkSize: 16, maxBytesPerStream: 4 * 16);

        pump.Register(1L, MakeBody(16 * 40), contentLength: null, CancellationToken.None);
        DrainToCompletion(pump, target, [1L]);

        Assert.Single(target.Completed);
        var total = target.Emitted.Where(e => !e.EndStream).Sum(e => e.Data.Length);
        Assert.Equal(16 * 40, total);
    }

    [Fact(Timeout = 5000)]
    public void Starving_one_stream_should_park_only_that_stream_while_the_other_drains()
    {
        var target = new FakeTarget();
        // Per-stream budget = 2 chunks (32 KB). Stream A is never credited; stream B is.
        var pump = MakePump(target, chunkSize: 16 * 1024, maxBytesPerStream: 32 * 1024);

        const long streamA = 1L;
        const long streamB = 3L;
        pump.Register(streamA, MakeBody(200 * 1024), contentLength: null, CancellationToken.None);
        pump.Register(streamB, MakeBody(200 * 1024), contentLength: null, CancellationToken.None);

        // Credit only B; A starves and parks once its initial 32 KB budget is spent.
        DrainToCompletion(pump, target, [streamB]);

        // A parked after at most its budget (one extra chunk tolerance).
        Assert.True(target.EmittedBytes(streamA) <= 32 * 1024 + 16 * 1024,
            $"stream A should have parked near its budget, emitted {target.EmittedBytes(streamA)}");
        Assert.False(target.EndStreamSeen(streamA), "starved stream A must NOT complete");
        // B drained fully.
        Assert.True(target.EndStreamSeen(streamB), "credited stream B should drain fully");
        Assert.Contains(streamB, target.Completed);
        Assert.DoesNotContain(streamA, target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void OnCapacityAvailable_should_resume_only_the_credited_stream()
    {
        var target = new FakeTarget();
        var pump = MakePump(target, chunkSize: 16 * 1024, maxBytesPerStream: 32 * 1024);

        const long streamA = 1L;
        const long streamB = 3L;
        pump.Register(streamA, MakeBody(200 * 1024), contentLength: null, CancellationToken.None);
        pump.Register(streamB, MakeBody(200 * 1024), contentLength: null, CancellationToken.None);

        DrainToCompletion(pump, target, [streamB]);

        var beforeA = target.EmittedBytes(streamA);
        var beforeB = target.EmittedBytes(streamB);

        // Credit ONLY A: exactly one more chunk of A must flow, nothing more for the completed B.
        pump.OnCapacityAvailable(streamA, 16 * 1024);
        DriveReadsWithoutCapacity(pump, target);

        Assert.True(target.EmittedBytes(streamA) > beforeA, "stream A should resume after being credited");
        Assert.Equal(beforeB, target.EmittedBytes(streamB));
    }

    [Fact(Timeout = 5000)]
    public void OnCapacityAvailable_should_clamp_budget_to_maxBytesPerStream()
    {
        var target = new FakeTarget();
        // Budget of one chunk. Over-credit far beyond the cap; only one further chunk may flow
        // before the stream parks again (clamp prevents runaway).
        var pump = MakePump(target, chunkSize: 16, maxBytesPerStream: 16);

        pump.Register(1L, MakeBody(16 * 40), contentLength: null, CancellationToken.None);
        DriveReadsWithoutCapacity(pump, target);
        var afterFirstPark = target.EmittedBytes(1L);
        Assert.Equal(16, afterFirstPark);

        // Grant a huge credit; clamp caps the budget at maxBytesPerStream (16 = one chunk).
        pump.OnCapacityAvailable(1L, 10 * 1024 * 1024);
        DriveReadsWithoutCapacity(pump, target);

        Assert.Equal(16 * 2, target.EmittedBytes(1L));
        Assert.Empty(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void OnCapacityAvailable_should_ignore_unknown_stream()
    {
        var target = new FakeTarget();
        var pump = MakePump(target);

        // No slot registered for 999 — must be a silent no-op.
        pump.OnCapacityAvailable(999L, 16 * 1024);

        Assert.Empty(target.Emitted);
        Assert.Empty(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void Reregistering_stream_after_reset_should_get_fresh_full_budget()
    {
        // Reconnect premise (Task 5/7 clarification 3): a replayed transfer re-registers the SAME
        // stream id (tracker resets to 0). The pump must hand it a FRESH slot at full budget so the
        // replay never deadlocks on the depleted budget of the interrupted transfer.
        var target = new FakeTarget();
        var pump = MakePump(target, chunkSize: 16, maxBytesPerStream: 4 * 16);

        // First transfer: starve it so its slot budget is driven to 0 and it parks.
        pump.Register(1L, MakeBody(16 * 40), contentLength: null, CancellationToken.None);
        DriveReadsWithoutCapacity(pump, target);
        Assert.True(target.EmittedBytes(1L) <= 4 * 16);
        Assert.Empty(target.Completed);

        // Re-register the SAME stream id with a fresh body (simulating replay). No explicit credit:
        // if the fresh slot did not start at full budget, this would emit nothing and never complete.
        target.Emitted.Clear();
        pump.Register(1L, MakeBody(16 * 3), contentLength: null, CancellationToken.None);
        DriveReadsWithoutCapacity(pump, target);

        Assert.Equal(16 * 3, target.EmittedBytes(1L));
        Assert.Single(target.Completed);
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
