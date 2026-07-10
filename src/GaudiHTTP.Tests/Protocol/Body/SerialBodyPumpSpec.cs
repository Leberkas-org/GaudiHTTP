using System.Buffers;
using System.IO.Pipelines;
using System.Reflection;
using Akka.Actor;
using GaudiHTTP.Protocol.Body;

namespace GaudiHTTP.Tests.Protocol.Body;

public sealed class SerialBodyPumpSpec
{
    private sealed class FakeTarget : IBodyDrainTarget
    {
        public List<(int StreamId, byte[] Data, bool EndStream)> Emitted { get; } = [];
        public List<int> Completed { get; } = [];
        public List<(int StreamId, Exception Reason)> Failed { get; } = [];
        public List<object> PendingMessages { get; } = [];
        public IActorRef StageActor => _interceptor;
        private readonly MessageInterceptor _interceptor;

        public FakeTarget()
        {
            _interceptor = new MessageInterceptor(PendingMessages);
        }

        public void EmitDataFrames(int streamId, ReadOnlyMemory<byte> data, bool endStream)
        {
            Emitted.Add((streamId, data.ToArray(), endStream));
        }

        public void OnDrainComplete(int streamId) => Completed.Add(streamId);
        public void OnDrainFailed(int streamId, Exception reason) => Failed.Add((streamId, reason));
    }

    private sealed class MessageInterceptor : MinimalActorRef
    {
        private readonly List<object> _messages;
        public MessageInterceptor(List<object> messages) => _messages = messages;
        public override ActorPath Path { get; } = new RootActorPath(new Address("akka", "test")) / "fake-serial";
        public override IActorRefProvider Provider => throw new NotSupportedException();
        protected override void TellInternal(object message, IActorRef sender) => _messages.Add(message);
    }

    private static void DrainToCompletion(
        SerialBodyPump pump,
        FakeTarget target,
        int maxIterations = 10_000)
    {
        var iterations = 0;
        while (target.Completed.Count == 0
               && target.Failed.Count == 0
               && iterations++ < maxIterations)
        {
            if (target.PendingMessages.Count == 0)
            {
                break;
            }

            var msg = target.PendingMessages[0];
            target.PendingMessages.RemoveAt(0);
            switch (msg)
            {
                case BodyReadComplete<int> rc:
                    pump.HandleReadComplete(rc.BytesRead);
                    break;
            }
        }
    }

    /// <summary>
    /// Target that calls OnCapacityAvailable() synchronously after EmitDataFrames,
    /// simulating a consumer that immediately signals capacity after each chunk.
    /// Uses a direct-dispatch actor ref so pump messages (BodyReadComplete)
    /// are processed inline as they arrive — no external drain loop needed.
    /// </summary>
    private sealed class AutoResumeTarget : IBodyDrainTarget
    {
        private SerialBodyPump? _pump;
        public List<(int StreamId, byte[] Data, bool EndStream)> Emitted { get; } = [];
        public List<int> Completed { get; } = [];
        public List<(int StreamId, Exception Reason)> Failed { get; } = [];
        public IActorRef StageActor => _directRef;
        private readonly DirectDispatchActorRef _directRef;

        public AutoResumeTarget()
        {
            _directRef = new DirectDispatchActorRef(this);
        }

        public void SetPump(SerialBodyPump pump) => _pump = pump;

        public void EmitDataFrames(int streamId, ReadOnlyMemory<byte> data, bool endStream)
        {
            Emitted.Add((streamId, data.ToArray(), endStream));
            if (!endStream)
            {
                // Model a consumer that flushes exactly the bytes just emitted and credits them back.
                _pump?.OnCapacityAvailable(data.Length);
            }
        }

        public void OnDrainComplete(int streamId) => Completed.Add(streamId);

        public void OnDrainFailed(int streamId, Exception reason) => Failed.Add((streamId, reason));

        private sealed class DirectDispatchActorRef(AutoResumeTarget owner) : MinimalActorRef
        {
            public override ActorPath Path { get; } = new RootActorPath(new Address("akka", "test")) / "fake-auto-resume";
            public override IActorRefProvider Provider => throw new NotSupportedException();

            protected override void TellInternal(object message, IActorRef sender)
            {
                if (owner._pump is null)
                {
                    return;
                }

                switch (message)
                {
                    case BodyReadComplete<int> rc:
                        owner._pump.HandleReadComplete(rc.BytesRead);
                        break;
                }
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

    private static SerialBodyPump MakePump(IBodyDrainTarget target, int chunkSize = 16 * 1024,
        int maxBytes = 256 * 1024)
    {
        return new SerialBodyPump(target, new CancellationTokenSource(), chunkSize, maxBytes);
    }

    private static long TotalEmittedBytes(FakeTarget target)
        => target.Emitted.Where(e => !e.EndStream).Sum(e => (long)e.Data.Length);

    // Processes every queued read completion WITHOUT granting additional credit, so the pump
    // advances only as far as its current byte budget allows before parking.
    private static void DrainPendingNoCredit(SerialBodyPump pump, FakeTarget target, int maxIterations = 10_000)
    {
        var iterations = 0;
        while (target.PendingMessages.Count > 0 && iterations++ < maxIterations)
        {
            var msg = target.PendingMessages[0];
            target.PendingMessages.RemoveAt(0);
            if (msg is BodyReadComplete<int> rc)
            {
                pump.HandleReadComplete(rc.BytesRead);
            }
        }
    }

    [Fact(Timeout = 5000)]
    public void Register_should_emit_body_immediately_for_sync_stream()
    {
        var target = new FakeTarget();
        var pump = MakePump(target);

        pump.Register(MakeBody(100), contentLength: null, CancellationToken.None);
        DrainToCompletion(pump, target);

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
        var pump = MakePump(target);

        pump.Register(new MemoryStream([]), contentLength: null, CancellationToken.None);
        DrainToCompletion(pump, target);

        Assert.Single(target.Emitted);
        Assert.True(target.Emitted[0].EndStream);
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void Pump_should_park_after_maxBytes_without_credit()
    {
        var target = new FakeTarget();
        var pump = MakePump(target, chunkSize: 16 * 1024, maxBytes: 32 * 1024);

        pump.Register(MakeBody(128 * 1024), contentLength: null, CancellationToken.None);
        DrainPendingNoCredit(pump, target);

        // No flush credit was granted: at most maxBytes (+ one chunk of overshoot) drains, and the
        // 128 KB body is NOT fully emitted (would be 8 chunks) — the pump is parked on budget.
        var emitted = TotalEmittedBytes(target);
        Assert.True(emitted <= 32 * 1024 + 16 * 1024,
            $"pump emitted {emitted} bytes without any flush — no byte backpressure.");
        Assert.True(emitted >= 32 * 1024,
            $"pump should drain up to the initial budget before parking, only emitted {emitted}.");
        Assert.Empty(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void OnCapacityAvailable_should_credit_bytes_and_clamp_to_maxBytes()
    {
        var target = new FakeTarget();
        var pump = MakePump(target, chunkSize: 16 * 1024, maxBytes: 32 * 1024);

        pump.Register(MakeBody(128 * 1024), contentLength: null, CancellationToken.None);
        DrainPendingNoCredit(pump, target);
        var afterPark = TotalEmittedBytes(target);

        // One flush of 16 KB drains one more chunk.
        pump.OnCapacityAvailable(16 * 1024);
        DrainPendingNoCredit(pump, target);
        var afterOneFlush = TotalEmittedBytes(target);

        Assert.True(afterOneFlush > afterPark, "one flush should resume the pump for one more chunk.");
        Assert.True(afterOneFlush <= 48 * 1024 + 16 * 1024,
            $"budget must stay clamped to maxBytes; emitted {afterOneFlush}.");

        // A giant credit is clamped to maxBytes, so at most one budget's worth (2 chunks) drains,
        // not the entire remaining body.
        pump.OnCapacityAvailable(1024 * 1024);
        DrainPendingNoCredit(pump, target);
        var afterGiantCredit = TotalEmittedBytes(target);

        Assert.True(afterGiantCredit - afterOneFlush <= 32 * 1024 + 16 * 1024,
            $"a credit larger than maxBytes must be clamped; drained {afterGiantCredit - afterOneFlush} at once.");
    }

    [Fact(Timeout = 5000)]
    public void ResetCredit_should_restore_full_budget_after_depletion()
    {
        var target = new FakeTarget();
        var pump = MakePump(target, chunkSize: 16 * 1024, maxBytes: 32 * 1024);

        pump.Register(MakeBody(128 * 1024), contentLength: null, CancellationToken.None);
        DrainPendingNoCredit(pump, target);
        var afterPark = TotalEmittedBytes(target);
        Assert.True(afterPark <= 32 * 1024 + 16 * 1024);

        // ResetCredit restores the budget to maxBytes, resuming another full budget's worth.
        pump.ResetCredit();
        DrainPendingNoCredit(pump, target);
        var afterReset = TotalEmittedBytes(target);

        Assert.True(afterReset >= afterPark + 32 * 1024,
            $"ResetCredit should restore a full {32 * 1024}-byte budget; only advanced {afterReset - afterPark}.");
        Assert.True(afterReset - afterPark <= 32 * 1024 + 16 * 1024,
            "ResetCredit must clamp to maxBytes, not open the floodgates.");
    }

    [Fact(Timeout = 5000)]
    public void OnCapacityAvailable_should_resume_drain_after_capacity_exhaustion()
    {
        var target = new FakeTarget();
        // maxBytes=16 (one chunk of credit) so only ~1 chunk is read before the budget is exhausted.
        var pump = MakePump(target, chunkSize: 16, maxBytes: 16);
        var body = MakeBody(200);

        pump.Register(body, contentLength: null, CancellationToken.None);
        // Process any dispatched messages (first read completes as message)
        while (target.PendingMessages.Count > 0)
        {
            var msg = target.PendingMessages[0];
            target.PendingMessages.RemoveAt(0);
            switch (msg)
            {
                case BodyReadComplete<int> rc:
                    pump.HandleReadComplete(rc.BytesRead);
                    break;
            }
        }

        // With maxCapacity=1 and 16-byte chunks, only 1 chunk was drained then paused
        var emittedData = target.Emitted.Where(e => !e.EndStream).ToList();
        Assert.True(emittedData.Count >= 1, "At least one chunk should have been emitted");

        // Pump more capacity until drain completes
        while (target.Completed.Count == 0)
        {
            pump.OnCapacityAvailable(16);
            // Drain any messages generated by OnCapacityAvailable
            while (target.PendingMessages.Count > 0)
            {
                var msg = target.PendingMessages[0];
                target.PendingMessages.RemoveAt(0);
                switch (msg)
                {
                    case BodyReadComplete<int> rc:
                        pump.HandleReadComplete(rc.BytesRead);
                        break;
                }
            }
        }

        Assert.Equal(200, target.Emitted.Where(e => !e.EndStream).Sum(e => e.Data.Length));
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void Register_should_drain_complete_body_with_auto_resume()
    {
        var target = new AutoResumeTarget();
        var pump = MakePump(target, chunkSize: 16 * 1024, maxBytes: 16 * 1024);
        target.SetPump(pump);
        var body = MakeBody(200);

        pump.Register(body, contentLength: null, CancellationToken.None);

        var dataEmits = target.Emitted.Where(e => !e.EndStream).ToList();
        Assert.Single(dataEmits);  // 200 bytes < 16 KB chunk = 1 emit
        Assert.Equal(200, dataEmits.Sum(e => e.Data.Length));
        Assert.True(target.Emitted[^1].EndStream);
        Assert.Single(target.Completed);
    }

    private static IMemoryOwner<byte>? GetActiveOwner(SerialBodyPump pump)
    {
        var field = typeof(SerialBodyPump).GetField("_activeOwner", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (IMemoryOwner<byte>?)field.GetValue(pump);
    }

    /// <summary>
    /// A stream whose ReadAsync blocks on a TaskCompletionSource and ignores the cancellation token,
    /// so the pump keeps a read parked in-flight until the test drives the completion by hand.
    /// </summary>
    private sealed class GatedReadStream(Task<int> gate) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => new(gate);

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact(Timeout = 5000)]
    public void Cancel_should_defer_owner_teardown_while_read_in_flight()
    {
        // Race R1/H1: Cancel must NOT dispose+null _activeOwner while a body read is still in flight
        // (the pending ReadAsync would write into a recycled pool array, and the late completion would
        // dereference a null owner -> NRE). Teardown must be deferred to the read completion, mirroring
        // the multiplexed pumps' MarkOrphaned/defer discipline.
        var target = new FakeTarget();
        var pump = MakePump(target);
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        pump.Register(new GatedReadStream(tcs.Task), contentLength: null, CancellationToken.None);

        // A read is parked in-flight, so the pump holds a rented owner.
        Assert.NotNull(GetActiveOwner(pump));

        pump.Cancel();

        // RED without the fix: Cancel disposed and nulled the owner mid-read. GREEN: deferred.
        Assert.NotNull(GetActiveOwner(pump));

        // The outstanding read now lands. It must not NRE and must not emit the cancelled body; the
        // owner is disposed exactly once here, on the deferred completion.
        pump.HandleReadComplete(64);

        Assert.Null(GetActiveOwner(pump));
        Assert.Empty(target.Emitted);
        Assert.Empty(target.Completed);
        Assert.Empty(target.Failed);
    }

    [Fact(Timeout = 5000)]
    public void Cleanup_should_defer_owner_teardown_while_read_in_flight()
    {
        // Same race as Cancel, on the connection-teardown path (Cleanup).
        var target = new FakeTarget();
        var pump = MakePump(target);
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        pump.Register(new GatedReadStream(tcs.Task), contentLength: null, CancellationToken.None);

        Assert.NotNull(GetActiveOwner(pump));

        pump.Cleanup();

        // RED without the fix: Cleanup disposed and nulled the owner mid-read. GREEN: deferred.
        Assert.NotNull(GetActiveOwner(pump));

        pump.HandleReadComplete(64);

        Assert.Null(GetActiveOwner(pump));
        Assert.Empty(target.Emitted);
        Assert.Empty(target.Completed);
        Assert.Empty(target.Failed);
    }

    [Fact(Timeout = 5000)]
    public void Cancel_should_stop_drain()
    {
        var target = new FakeTarget();
        var pump = MakePump(target);

        pump.Register(MakeBody(100), contentLength: null, CancellationToken.None);
        DrainToCompletion(pump, target);
        Assert.Single(target.Completed);

        // Cancel after complete should be no-op (stream already null)
        pump.Cancel();
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
    public void Register_should_drain_small_body_without_additional_capacity()
    {
        var target = new FakeTarget();
        var pump = MakePump(target, chunkSize: 16 * 1024, maxBytes: 64 * 1024);
        var body = MakeBody(64);

        pump.Register(body, contentLength: null, CancellationToken.None);
        DrainToCompletion(pump, target);

        Assert.Equal(64, target.Emitted.Where(e => !e.EndStream).Sum(e => e.Data.Length));
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void Large_body_should_drain_fully_with_auto_resume()
    {
        var target = new AutoResumeTarget();
        var pump = MakePump(target, chunkSize: 16 * 1024, maxBytes: 16 * 1024);
        target.SetPump(pump);
        var largeBodySize = 1000 * 1024;
        var body = MakeBody(largeBodySize);

        pump.Register(body, contentLength: null, CancellationToken.None);

        Assert.Single(target.Completed);
        Assert.Equal(largeBodySize, target.Emitted.Where(e => !e.EndStream).Sum(e => e.Data.Length));
    }

    [Fact(Timeout = 5000)]
    public void Many_consecutive_sync_reads_should_drain_fully_with_auto_resume()
    {
        var target = new AutoResumeTarget();
        // Small chunk so the body needs dozens of consecutive sync reads to drain.
        var pump = MakePump(target, chunkSize: 16, maxBytes: 16);
        target.SetPump(pump);
        var totalSize = 65 * 16;
        var body = MakeBody(totalSize);

        pump.Register(body, contentLength: null, CancellationToken.None);

        var emittedBytes = target.Emitted.Where(e => !e.EndStream).Sum(e => e.Data.Length);
        Assert.Equal(totalSize, emittedBytes);
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void Capacity_grant_during_pending_completion_must_not_corrupt_buffer()
    {
        // Regression: force-async defers each sync read's completion to the mailbox while the
        // bytes still live in the single reused _buffer. A capacity grant (OnCapacityAvailable)
        // arriving in that window must NOT start the next read and overwrite _buffer before the
        // queued BodyReadComplete emits it. The pump keeps _isReadInFlight across the hop to
        // prevent exactly this; without it, >64 KB bodies corrupt over H1.
        var target = new FakeTarget();
        var pump = MakePump(target, chunkSize: 16, maxBytes: 16);
        var totalSize = 16 * 8; // 8 distinct chunks
        var body = MakeBody(totalSize);

        pump.Register(body, contentLength: null, CancellationToken.None);

        // Adversarial schedule: inject a capacity grant before draining each queued completion,
        // i.e. exactly while a completion that still references _buffer is in flight.
        var guard = 0;
        while (target.Completed.Count == 0 && guard++ < 10_000)
        {
            pump.OnCapacityAvailable(16);
            if (target.PendingMessages.Count == 0)
            {
                continue;
            }

            var msg = target.PendingMessages[0];
            target.PendingMessages.RemoveAt(0);
            switch (msg)
            {
                case BodyReadComplete<int> rc:
                    pump.HandleReadComplete(rc.BytesRead);
                    break;
            }
        }

        // The reassembled body must equal the original, in order — no chunk overwritten.
        var reassembled = target.Emitted.Where(e => !e.EndStream).SelectMany(e => e.Data).ToArray();
        Assert.Equal(MakeBody(totalSize).ToArray(), reassembled);
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void HandleReadComplete_should_complete_drain()
    {
        var target = new FakeTarget();
        var pump = MakePump(target, chunkSize: 16, maxBytes: 16);

        var neverStream = new NeverReadStream();
        pump.Register(neverStream, contentLength: null, CancellationToken.None);

        // Simulate async read completing with EOF
        pump.HandleReadComplete(0);

        Assert.Single(target.Completed);
        Assert.Single(target.Emitted);
        Assert.True(target.Emitted[0].EndStream);
    }

    [Fact(Timeout = 5000)]
    public void HandleReadFailed_should_report_failure()
    {
        var target = new FakeTarget();
        var pump = MakePump(target, chunkSize: 16, maxBytes: 16);

        var neverStream = new NeverReadStream();
        pump.Register(neverStream, contentLength: null, CancellationToken.None);

        var ex = new IOException("simulated read failure");
        pump.HandleReadFailed(ex);

        Assert.Single(target.Failed);
        Assert.Same(ex, target.Failed[0].Reason);
        Assert.Empty(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void CtsDisposal_should_not_throw_after_drain()
    {
        var target = new FakeTarget();
        var pump = MakePump(target);
        var reqCts = new CancellationTokenSource();

        pump.Register(MakeBody(100), contentLength: null, reqCts.Token);
        DrainToCompletion(pump, target);

        Assert.Single(target.Completed);
        // Verify no exception when disposing reqCts (linked CTS should already be disposed by pump)
        reqCts.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void PipeReader_should_drain_completed_pipe_with_auto_resume()
    {
        // Scenario: PipeWriter writes 64 KB in 1 KB chunks, then completes.
        // PipeReader.AsStream() is registered AFTER all data is written.
        // All reads should complete synchronously since data is already buffered.
        var pipeOptions = new PipeOptions(pauseWriterThreshold: 0, resumeWriterThreshold: 0);
        var pipe = new Pipe(pipeOptions);
        var totalSize = 64 * 1024;
        var chunkSize = 1024;

        for (var i = 0; i < totalSize / chunkSize; i++)
        {
            var mem = pipe.Writer.GetMemory(chunkSize);
            for (var j = 0; j < chunkSize; j++)
            {
                mem.Span[j] = (byte)((i * chunkSize + j) % 256);
            }

            pipe.Writer.Advance(chunkSize);
            var flushResult = pipe.Writer.FlushAsync(TestContext.Current.CancellationToken);
            Assert.True(flushResult.IsCompleted, "FlushAsync should complete synchronously with no back-pressure");
        }

        pipe.Writer.Complete();

        var target = new AutoResumeTarget();
        var pump = MakePump(target, chunkSize: 16 * 1024, maxBytes: 16 * 1024);
        target.SetPump(pump);

        var bodyStream = pipe.Reader.AsStream();
        pump.Register(bodyStream, contentLength: null, CancellationToken.None);

        var emittedBytes = target.Emitted.Where(e => !e.EndStream).Sum(e => e.Data.Length);
        Assert.Equal(totalSize, emittedBytes);
        Assert.Single(target.Completed);
        Assert.Empty(target.Failed);
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
