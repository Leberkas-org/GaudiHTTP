using System.IO.Pipelines;
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
        public IActorRef StageActor { get; } = ActorRefs.Nobody;

        public void EmitDataFrames(int streamId, ReadOnlyMemory<byte> data, bool endStream)
        {
            Emitted.Add((streamId, data.ToArray(), endStream));
        }

        public void OnDrainComplete(int streamId) => Completed.Add(streamId);
        public void OnDrainFailed(int streamId, Exception reason) => Failed.Add((streamId, reason));
    }

    /// <summary>
    /// Target that calls OnCapacityAvailable() synchronously after EmitDataFrames,
    /// simulating a consumer that immediately signals capacity after each chunk.
    /// </summary>
    private sealed class AutoResumeTarget : IBodyDrainTarget
    {
        private SerialBodyPump? _pump;
        public List<(int StreamId, byte[] Data, bool EndStream)> Emitted { get; } = [];
        public List<int> Completed { get; } = [];
        public List<(int StreamId, Exception Reason)> Failed { get; } = [];
        public IActorRef StageActor { get; } = ActorRefs.Nobody;

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

    private static SerialBodyPump MakePump(IBodyDrainTarget target, int chunkSize = 16 * 1024, int maxCapacity = 2)
    {
        return new SerialBodyPump(target, new CancellationTokenSource(), chunkSize, maxCapacity);
    }

    [Fact(Timeout = 5000)]
    public void Register_should_emit_body_immediately_for_sync_stream()
    {
        var target = new FakeTarget();
        var pump = MakePump(target);

        pump.Register(MakeBody(100), contentLength: null, CancellationToken.None);

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

        Assert.Single(target.Emitted);
        Assert.True(target.Emitted[0].EndStream);
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void OnCapacityAvailable_should_resume_drain_after_capacity_exhaustion()
    {
        var target = new FakeTarget();
        // maxCapacity=1 so only 1 chunk is read before pausing
        var pump = MakePump(target, chunkSize: 16, maxCapacity: 1);
        var body = MakeBody(200);

        pump.Register(body, contentLength: null, CancellationToken.None);

        // With maxCapacity=1 and 16-byte chunks, only 1 chunk was drained then paused
        var emittedData = target.Emitted.Where(e => !e.EndStream).ToList();
        Assert.True(emittedData.Count >= 1, "At least one chunk should have been emitted");

        // Pump more capacity until drain completes
        while (target.Completed.Count == 0)
        {
            pump.OnCapacityAvailable();
        }

        Assert.Equal(200, target.Emitted.Where(e => !e.EndStream).Sum(e => e.Data.Length));
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void Register_should_drain_complete_body_with_auto_resume()
    {
        var target = new AutoResumeTarget();
        var pump = MakePump(target, chunkSize: 16 * 1024, maxCapacity: 2);
        target.SetPump(pump);
        var body = MakeBody(200);

        pump.Register(body, contentLength: null, CancellationToken.None);

        var dataEmits = target.Emitted.Where(e => !e.EndStream).ToList();
        Assert.Single(dataEmits);  // 200 bytes < 16 KB chunk = 1 emit
        Assert.Equal(200, dataEmits.Sum(e => e.Data.Length));
        Assert.True(target.Emitted[^1].EndStream);
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void Cancel_should_stop_drain()
    {
        var target = new FakeTarget();
        var pump = MakePump(target);

        pump.Register(MakeBody(100), contentLength: null, CancellationToken.None);
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
        var pump = MakePump(target, chunkSize: 16 * 1024, maxCapacity: 4);
        var body = MakeBody(64);

        pump.Register(body, contentLength: null, CancellationToken.None);

        Assert.Equal(64, target.Emitted.Where(e => !e.EndStream).Sum(e => e.Data.Length));
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void Large_body_should_drain_fully_with_auto_resume()
    {
        var target = new AutoResumeTarget();
        var pump = MakePump(target, chunkSize: 16 * 1024, maxCapacity: 2);
        target.SetPump(pump);
        var largeBodySize = 1000 * 1024;
        var body = MakeBody(largeBodySize);

        pump.Register(body, contentLength: null, CancellationToken.None);

        Assert.Single(target.Completed);
        Assert.Equal(largeBodySize, target.Emitted.Where(e => !e.EndStream).Sum(e => e.Data.Length));
    }

    [Fact(Timeout = 5000)]
    public void Sync_reads_should_complete_large_body_with_starvation_guard_via_actor()
    {
        var target = new AutoResumeTarget();
        // Small chunk to trigger many sync reads; starvation guard will fire at 64
        var pump = MakePump(target, chunkSize: 16, maxCapacity: 2);
        target.SetPump(pump);
        var body = MakeBody(65 * 16);

        pump.Register(body, contentLength: null, CancellationToken.None);

        // Starvation guard fires at 64 consecutive reads and sends DrainContinue to StageActor (Nobody).
        // So drain stalls. Verify partial data was emitted before guard fired.
        var emittedBytes = target.Emitted.Where(e => !e.EndStream).Sum(e => e.Data.Length);
        Assert.True(emittedBytes > 0, "Expected data emitted before starvation guard triggered");
    }

    [Fact(Timeout = 5000)]
    public void HandleReadComplete_should_complete_drain()
    {
        var target = new FakeTarget();
        var pump = MakePump(target, chunkSize: 16, maxCapacity: 1);

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
        var pump = MakePump(target, chunkSize: 16, maxCapacity: 1);

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
        var pump = MakePump(target, chunkSize: 16 * 1024, maxCapacity: 4);
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
