using System.Buffers;
using System.IO.Pipelines;
using Akka.Actor;
using GaudiHTTP.Pooling;
using GaudiHTTP.Protocol.Body;

namespace GaudiHTTP.Tests.Protocol.Body;

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
    /// Target that calls AddCredit() synchronously after EmitDataFrames,
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
                _pump?.AddCredit();
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
        var target = new FakeTarget();
        var poolContext = new ConnectionPoolContext();
        var connCts = new CancellationTokenSource();
        var pump = new SerialBodyPump(target, poolContext, connCts);
        var body = MakeBody(100);

        pump.Register(body, CancellationToken.None);

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
        var poolContext = new ConnectionPoolContext();
        var connCts = new CancellationTokenSource();
        var pump = new SerialBodyPump(target, poolContext, connCts);
        var body = new MemoryStream([]);

        pump.Register(body, CancellationToken.None);

        Assert.Single(target.Emitted);
        Assert.True(target.Emitted[0].EndStream);
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void Register_should_emit_complete_body_with_auto_resume()
    {
        // Register with initial credits drains small body immediately.
        // AutoResumeTarget is not actually needed now that we have initial credits.
        var target = new AutoResumeTarget();
        var poolContext = new ConnectionPoolContext();
        var connCts = new CancellationTokenSource();
        var pump = new SerialBodyPump(target, poolContext, connCts);
        target.SetPump(pump);
        var body = MakeBody(200);

        pump.Register(body, CancellationToken.None);

        var dataEmits = target.Emitted.Where(e => !e.EndStream).ToList();
        Assert.Single(dataEmits);  // 200 bytes < 16 KB chunk = 1 emit
        Assert.Equal(200, dataEmits.Sum(e => e.Data.Length));
        Assert.True(target.Emitted[^1].EndStream);
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void AddCredit_should_resume_after_budget_exhaustion()
    {
        var target = new FakeTarget();
        var poolContext = new ConnectionPoolContext();
        var connCts = new CancellationTokenSource();
        var pump = new SerialBodyPump(target, poolContext, connCts);
        var body = MakeBody(200);

        pump.Register(body, CancellationToken.None);

        // Initial register starts reads. With target's 16KB chunk size and FakeTarget (no auto-resume),
        // the base class credit system drains initial budget and pauses.
        // We need to add credits to resume.
        while (target.Completed.Count == 0)
        {
            pump.AddCredit();
        }

        Assert.Equal(200, target.Emitted.Where(e => !e.EndStream).Sum(e => e.Data.Length));
    }

    [Fact(Timeout = 5000)]
    public void Cancel_should_stop_drain()
    {
        var target = new FakeTarget();
        var poolContext = new ConnectionPoolContext();
        var connCts = new CancellationTokenSource();
        var pump = new SerialBodyPump(target, poolContext, connCts);
        var body = MakeBody(100);

        pump.Register(body, CancellationToken.None);
        // Already completed since MemoryStream is sync with sufficient budget
        Assert.Single(target.Completed);

        // Cancel after complete should be no-op
        pump.Cancel(0);
    }

    [Fact(Timeout = 5000)]
    public void Cancel_midDrain_should_not_call_onDrainComplete()
    {
        var target = new FakeTarget();
        var poolContext = new ConnectionPoolContext();
        var connCts = new CancellationTokenSource();
        var pump = new SerialBodyPump(target, poolContext, connCts);
        // Large enough to not complete with 16 initial credits
        var largeBody = MakeBody(1000 * 1024);

        pump.Register(largeBody, CancellationToken.None);
        // Some reads completed with initial credits
        var initialEmitted = target.Emitted.Count(e => !e.EndStream);
        Assert.True(initialEmitted > 0, "initial credits should have started reads");
        Assert.Empty(target.Completed);

        // Cancel mid-drain
        pump.Cancel(0);

        // Cancel should NOT fire OnDrainComplete (only OnDrainFailed if read in-flight)
        // With sync MemoryStream, all reads already completed, so nothing in-flight
        var completedAfterCancel = target.Completed;
        var failedAfterCancel = target.Failed;
        Assert.Empty(completedAfterCancel);
        Assert.Empty(failedAfterCancel);
    }

    [Fact(Timeout = 5000)]
    public void Cleanup_should_be_idempotent()
    {
        var target = new FakeTarget();
        var poolContext = new ConnectionPoolContext();
        var connCts = new CancellationTokenSource();
        var pump = new SerialBodyPump(target, poolContext, connCts);

        pump.Cleanup();
        pump.Cleanup();
    }

    [Fact(Timeout = 5000)]
    public void Register_should_drain_small_body_without_additional_credits()
    {
        // Small body fits within initial budget without needing additional AddCredit calls.
        var target = new FakeTarget();
        var poolContext = new ConnectionPoolContext();
        var connCts = new CancellationTokenSource();
        var pump = new SerialBodyPump(target, poolContext, connCts);
        var body = MakeBody(64);

        pump.Register(body, CancellationToken.None);

        Assert.Equal(64, target.Emitted.Where(e => !e.EndStream).Sum(e => e.Data.Length));
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void AddCredit_should_drain_large_body_without_limit()
    {
        // Very large body: initial 16 credits may not be enough depending on budget.
        // Keep adding credits until drain completes.
        var target = new FakeTarget();
        var poolContext = new ConnectionPoolContext();
        var connCts = new CancellationTokenSource();
        var pump = new SerialBodyPump(target, poolContext, connCts);
        var largeBodySize = 1000 * 1024;  // 1 MB body
        var body = MakeBody(largeBodySize);

        pump.Register(body, CancellationToken.None);

        // With 16 initial credits and 16 KB chunks, we drain ~256 KB. Need more credits for 1 MB.
        int iterations = 0;
        while (target.Completed.Count == 0 && iterations < 1000)
        {
            pump.AddCredit();
            iterations++;
        }

        Assert.True(iterations < 1000, "drain should complete within 1000 AddCredit calls");
        Assert.Single(target.Completed);
        Assert.Equal(largeBodySize, target.Emitted.Where(e => !e.EndStream).Sum(e => e.Data.Length));
    }

    [Fact(Timeout = 5000)]
    public void Sync_reads_should_complete_large_body_with_auto_resume()
    {
        // Use AutoResumeTarget so the pump adds credit inline after each emit.
        // Large body drains synchronously.
        var target = new AutoResumeTarget();
        var poolContext = new ConnectionPoolContext();
        var connCts = new CancellationTokenSource();
        var pump = new SerialBodyPump(target, poolContext, connCts);
        target.SetPump(pump);
        var body = MakeBody(65 * 16);

        pump.Register(body, CancellationToken.None);

        // All 65 data chunks emitted + EOF
        var emittedBytes = target.Emitted.Where(e => !e.EndStream).Sum(e => e.Data.Length);
        Assert.Equal(65 * 16, emittedBytes);
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void CtsDisposal_should_happen_on_complete()
    {
        var target = new FakeTarget();
        var poolContext = new ConnectionPoolContext();
        var connCts = new CancellationTokenSource();
        var reqCts = new CancellationTokenSource();
        var pump = new SerialBodyPump(target, poolContext, connCts);

        pump.Register(MakeBody(100), reqCts.Token);

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
        //
        // PauseWriterThreshold = 0 disables writer back-pressure so all FlushAsync
        // calls complete synchronously without a reader consuming data first.
        var pipeOptions = new PipeOptions(pauseWriterThreshold: 0, resumeWriterThreshold: 0);
        var pipe = new Pipe(pipeOptions);
        var totalSize = 64 * 1024;
        var chunkSize = 1024;

        // Write all data first — FlushAsync completes synchronously with no back-pressure
        for (var i = 0; i < totalSize / chunkSize; i++)
        {
            var mem = pipe.Writer.GetMemory(chunkSize);
            for (var j = 0; j < chunkSize; j++)
            {
                mem.Span[j] = (byte)((i * chunkSize + j) % 256);
            }

            pipe.Writer.Advance(chunkSize);
            var flushResult = pipe.Writer.FlushAsync();
            Assert.True(flushResult.IsCompleted, "FlushAsync should complete synchronously with no back-pressure");
        }

        pipe.Writer.Complete();

        // Now register the pump with the completed pipe
        var target = new AutoResumeTarget();
        var poolContext = new ConnectionPoolContext();
        var connCts = new CancellationTokenSource();
        var pump = new SerialBodyPump(target, poolContext, connCts);
        target.SetPump(pump);

        var bodyStream = pipe.Reader.AsStream();
        pump.Register(bodyStream, CancellationToken.None);

        var emittedBytes = target.Emitted.Where(e => !e.EndStream).Sum(e => e.Data.Length);
        Assert.Equal(totalSize, emittedBytes);
        Assert.Single(target.Completed);
        Assert.Empty(target.Failed);
    }

    [Fact(Timeout = 5000)]
    public async Task PipeReader_should_drain_pipe_when_writer_completes_after_registration()
    {
        // Scenario: Write a few chunks, register the pump, then write the rest.
        // The first reads complete synchronously (data already buffered).
        // Later reads may go async because the PipeWriter hasn't written yet.
        //
        // This simulates the real server handler case: the handler writes to
        // PipeWriter while the pump reads from PipeReader.AsStream() concurrently.
        var pipeOptions = new PipeOptions(pauseWriterThreshold: 0, resumeWriterThreshold: 0);
        var pipe = new Pipe(pipeOptions);
        var totalSize = 64 * 1024;
        var chunkSize = 1024;
        var preWriteChunks = 4; // Write 4 KB before registering

        // Write initial chunks
        for (var i = 0; i < preWriteChunks; i++)
        {
            var mem = pipe.Writer.GetMemory(chunkSize);
            for (var j = 0; j < chunkSize; j++)
            {
                mem.Span[j] = (byte)((i * chunkSize + j) % 256);
            }

            pipe.Writer.Advance(chunkSize);
            var flushResult = pipe.Writer.FlushAsync();
            Assert.True(flushResult.IsCompleted);
        }

        // Register the pump BEFORE writer completes
        var target = new AutoResumeTarget();
        var poolContext = new ConnectionPoolContext();
        var connCts = new CancellationTokenSource();
        var pump = new SerialBodyPump(target, poolContext, connCts);
        target.SetPump(pump);

        var bodyStream = pipe.Reader.AsStream();
        pump.Register(bodyStream, CancellationToken.None);

        // At this point, the pump has consumed the initial 4 KB synchronously,
        // then issued a read that went async (no more data in pipe yet).
        var emittedSoFar = target.Emitted.Where(e => !e.EndStream).Sum(e => e.Data.Length);
        Assert.True(emittedSoFar >= preWriteChunks * chunkSize,
            $"Expected at least {preWriteChunks * chunkSize} bytes emitted, got {emittedSoFar}");
        Assert.Empty(target.Completed); // Not done yet — writer hasn't completed

        // Now write the remaining chunks from a background task.
        // The async reads dispatched via PipeTo need an actor to receive the messages.
        // Since we don't have an actor system in this unit test, the PipeTo target is
        // ActorRefs.Nobody — async reads will be lost.
        //
        // This proves the core issue: when PipeReader.AsStream().ReadAsync() goes async,
        // the SerialBodyPump dispatches via PipeTo to ActorRefs.Nobody, and the drain stalls.
        await Task.Run(async () =>
        {
            for (var i = preWriteChunks; i < totalSize / chunkSize; i++)
            {
                var mem = pipe.Writer.GetMemory(chunkSize);
                for (var j = 0; j < chunkSize; j++)
                {
                    mem.Span[j] = (byte)((i * chunkSize + j) % 256);
                }

                pipe.Writer.Advance(chunkSize);
                await pipe.Writer.FlushAsync();
            }

            pipe.Writer.Complete();
        });

        // Give a short window for any async completions to arrive
        await Task.Delay(100);

        // The pump is stalled — no actor receives the PipeTo messages.
        // With a real actor system, HandleReadComplete would be called, but here
        // the drain should NOT have completed because PipeTo goes to Nobody.
        var finalBytes = target.Emitted.Where(e => !e.EndStream).Sum(e => e.Data.Length);
        Assert.True(finalBytes < totalSize,
            $"Expected drain to stall (async reads lost to Nobody), but got {finalBytes}/{totalSize} bytes. " +
            "If this unexpectedly passes, PipeReader.AsStream() may be completing reads synchronously " +
            "even when data arrives after the read was issued.");
        Assert.Empty(target.Completed);
    }
}
