using Akka.Actor;
using GaudiHTTP.Pooling;
using GaudiHTTP.Protocol.Body;
using GaudiHTTP.Protocol.Syntax.Http2;

namespace GaudiHTTP.Tests.Protocol.Body;

public sealed class BodyDrainSchedulerSpec
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
        public override ActorPath Path { get; } = new RootActorPath(new Address("akka", "test")) / "fake";
        public override IActorRefProvider Provider => throw new NotSupportedException();
        protected override void TellInternal(object message, IActorRef sender) => _messages.Add(message);
    }

    private sealed class DelegatingReadStream(Func<Memory<byte>, CancellationToken, ValueTask<int>> readFunc) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => readFunc(buffer, cancellationToken);
    }

    private static FlowController MakeFlow(int connWindow = 1024 * 1024)
    {
        var fc = new FlowController(1024 * 1024, 64 * 1024);
        if (connWindow != 65535)
        {
            fc.OnSendWindowUpdate(0, connWindow - 65535);
        }

        return fc;
    }

    private static void DrainToCompletion(BodyDrainScheduler scheduler, FakeTarget target, int expectedCompletions = 1, int maxIterations = 10_000)
    {
        var iterations = 0;
        while (target.Completed.Count < expectedCompletions && target.Failed.Count == 0 && iterations++ < maxIterations)
        {
            if (target.PendingMessages.Count == 0)
            {
                break;
            }

            var msg = target.PendingMessages[0];
            target.PendingMessages.RemoveAt(0);
            switch (msg)
            {
                case DrainReadComplete rc:
                    scheduler.HandleReadComplete(rc.StreamId, rc.BytesRead);
                    break;
                case DrainContinue dc:
                    scheduler.HandleDrainContinue(dc.StreamId);
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

    [Fact(Timeout = 5000)]
    public void Register_should_emit_body_when_window_available()
    {
        var target = new FakeTarget();
        var flow = MakeFlow();
        var scheduler = new BodyDrainScheduler(target, flow, new CancellationTokenSource(), new ConnectionPoolContext(), chunkSize: 1 * 1024, hardCap: 16);

        flow.InitStreamSendWindow(1);
        scheduler.Register(1, MakeBody(100), 100, CancellationToken.None);
        DrainToCompletion(scheduler, target);

        Assert.Equal(2, target.Emitted.Count);
        Assert.Equal(100, target.Emitted[0].Data.Length);
        Assert.False(target.Emitted[0].EndStream);
        Assert.True(target.Emitted[1].EndStream);
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void Register_should_block_when_stream_window_zero()
    {
        var target = new FakeTarget();
        var flow = MakeFlow();
        var scheduler = new BodyDrainScheduler(target, flow, new CancellationTokenSource(), new ConnectionPoolContext(), chunkSize: 1 * 1024, hardCap: 16);

        flow.InitStreamSendWindow(1);
        flow.OnDataSent(1, 65535);
        // Stream window is now 0
        scheduler.Register(1, MakeBody(100), 100, CancellationToken.None);

        Assert.Empty(target.Emitted);
        Assert.Empty(target.Completed);

        // WINDOW_UPDATE for stream 1
        flow.OnSendWindowUpdate(1, 65535);
        scheduler.OnWindowUpdate(1);
        DrainToCompletion(scheduler, target);

        Assert.Equal(2, target.Emitted.Count);
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void WindowBlocked_should_defer_read_until_window_opens()
    {
        var target = new FakeTarget();
        var flow = MakeFlow(connWindow: 65535);
        var scheduler = new BodyDrainScheduler(target, flow, new CancellationTokenSource(), new ConnectionPoolContext(), chunkSize: 1 * 1024, hardCap: 16);

        flow.InitStreamSendWindow(1);
        // Exhaust connection window completely
        flow.OnDataSent(1, 65535);

        scheduler.Register(1, MakeBody(200), 200, CancellationToken.None);

        // No reads should have started: connection window = 0
        Assert.Empty(target.Emitted);
        Assert.Empty(target.Completed);

        // Open both windows
        flow.OnSendWindowUpdate(1, 65535);
        flow.OnSendWindowUpdate(0, 65535);
        scheduler.OnWindowUpdate(0);
        DrainToCompletion(scheduler, target);

        var totalData = target.Emitted.Where(e => !e.EndStream).Sum(e => e.Data.Length);
        Assert.Equal(200, totalData);
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void RoundRobin_should_interleave_two_streams()
    {
        var target = new FakeTarget();
        var flow = MakeFlow();
        var scheduler = new BodyDrainScheduler(target, flow, new CancellationTokenSource(), new ConnectionPoolContext(), chunkSize: 64, hardCap: 16);

        flow.InitStreamSendWindow(1);
        flow.InitStreamSendWindow(3);

        scheduler.Register(1, MakeBody(128), 128, CancellationToken.None);
        scheduler.Register(3, MakeBody(128), 128, CancellationToken.None);
        DrainToCompletion(scheduler, target, expectedCompletions: 2);

        Assert.Contains(1, target.Completed);
        Assert.Contains(3, target.Completed);

        // Verify interleaving: stream IDs should alternate
        var streamIds = target.Emitted.Where(e => !e.EndStream).Select(e => e.StreamId).ToList();
        Assert.Contains(1, streamIds);
        Assert.Contains(3, streamIds);
    }

    [Fact(Timeout = 5000)]
    public void Orphan_should_not_crash_on_callback_after_cancel()
    {
        var target = new FakeTarget();
        var flow = MakeFlow();
        var scheduler = new BodyDrainScheduler(target, flow, new CancellationTokenSource(), new ConnectionPoolContext(), chunkSize: 1 * 1024, hardCap: 16);

        flow.InitStreamSendWindow(1);
        scheduler.Register(1, MakeBody(100), 100, CancellationToken.None);

        // Already completed for sync stream
        scheduler.Cancel(1);
        // No exception
    }

    [Fact(Timeout = 5000)]
    public void Cleanup_should_be_idempotent()
    {
        var target = new FakeTarget();
        var flow = MakeFlow();
        var scheduler = new BodyDrainScheduler(target, flow, new CancellationTokenSource(), new ConnectionPoolContext(), chunkSize: 1 * 1024, hardCap: 16);

        scheduler.Cleanup();
        scheduler.Cleanup();
    }

    [Fact(Timeout = 5000)]
    public void SyncFastPath_should_drain_without_PipeTo()
    {
        var target = new FakeTarget();
        var flow = MakeFlow();
        var scheduler = new BodyDrainScheduler(target, flow, new CancellationTokenSource(), new ConnectionPoolContext(), chunkSize: 1 * 1024, hardCap: 16);

        flow.InitStreamSendWindow(1);
        scheduler.Register(1, MakeBody(100), 100, CancellationToken.None);
        DrainToCompletion(scheduler, target);

        Assert.Equal(2, target.Emitted.Count);
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void ForceAsync_should_dispatch_sync_reads_via_message()
    {
        var target = new FakeTarget();
        var flow = MakeFlow(connWindow: 1024 * 1024);
        var scheduler = new BodyDrainScheduler(target, flow, new CancellationTokenSource(), new ConnectionPoolContext(), chunkSize: 16, hardCap: 16);

        flow.InitStreamSendWindow(1);
        flow.OnSendWindowUpdate(1, 1024 * 1024);
        scheduler.Register(1, MakeBody(65 * 16), 65 * 16, CancellationToken.None);

        // With force-async, every sync read dispatches a message instead of processing inline.
        // After Register, nothing is emitted yet — all reads are pending as messages.
        Assert.Empty(target.Completed);
        Assert.True(target.PendingMessages.Count > 0, "Sync reads should be dispatched as messages");

        // Drain all pending messages to completion
        DrainToCompletion(scheduler, target);

        var totalBytes = target.Emitted.Where(e => !e.EndStream).Sum(e => e.Data.Length);
        Assert.Equal(65 * 16, totalBytes);
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionWindowCeiling_should_cap_effectiveSlots()
    {
        var target = new FakeTarget();
        // Start with very small connection window
        var flow = MakeFlow(connWindow: 65535);
        var scheduler = new BodyDrainScheduler(target, flow, new CancellationTokenSource(), new ConnectionPoolContext(), chunkSize: 16 * 1024, hardCap: 16);

        // Connection window = 65535, chunkSize = 16384
        // With reservation, reads are bounded by min(chunkSize, streamWindow, connWindow)
        flow.InitStreamSendWindow(1);
        scheduler.Register(1, MakeBody(100), 100, CancellationToken.None);
        DrainToCompletion(scheduler, target);

        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void SlotPooling_should_reuse_slots()
    {
        var target = new FakeTarget();
        var flow = MakeFlow();
        var scheduler = new BodyDrainScheduler(target, flow, new CancellationTokenSource(), new ConnectionPoolContext(), chunkSize: 1 * 1024, hardCap: 16);

        flow.InitStreamSendWindow(1);
        scheduler.Register(1, MakeBody(10), 10, CancellationToken.None);
        DrainToCompletion(scheduler, target);
        Assert.Single(target.Completed);

        // Register again — should reuse pooled slot
        flow.InitStreamSendWindow(3);
        target.Completed.Clear();
        scheduler.Register(3, MakeBody(10), 10, CancellationToken.None);
        DrainToCompletion(scheduler, target);
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void CtsLifecycle_should_create_once_and_dispose_on_complete()
    {
        var target = new FakeTarget();
        var flow = MakeFlow();
        var connCts = new CancellationTokenSource();
        var reqCts = new CancellationTokenSource();
        var scheduler = new BodyDrainScheduler(target, flow, connCts, new ConnectionPoolContext(), chunkSize: 1 * 1024, hardCap: 16);

        flow.InitStreamSendWindow(1);
        scheduler.Register(1, MakeBody(100), 100, reqCts.Token);
        DrainToCompletion(scheduler, target);

        Assert.Single(target.Completed);
        reqCts.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void WindowReservation_should_consume_window_before_read_and_refund_unused()
    {
        var target = new FakeTarget();
        var flow = MakeFlow(connWindow: 65535);
        var scheduler = new BodyDrainScheduler(target, flow, new CancellationTokenSource(), new ConnectionPoolContext(), chunkSize: 1 * 1024, hardCap: 16);

        flow.InitStreamSendWindow(1);
        var windowBefore = flow.ConnectionSendWindow;

        // Body is smaller than chunkSize: reserve 1024, read 100, expect refund of 924
        scheduler.Register(1, MakeBody(100), 100, CancellationToken.None);
        DrainToCompletion(scheduler, target);

        // After completion, window should be reduced by exactly 100 (bytes actually sent),
        // not by 1024 (the reserved chunk size).
        Assert.Equal(windowBefore - 100, flow.ConnectionSendWindow);
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void OrphanRefund_should_return_reserved_window_when_stream_cancelled_during_read()
    {
        var target = new FakeTarget();
        var flow = MakeFlow();
        var scheduler = new BodyDrainScheduler(target, flow, new CancellationTokenSource(), new ConnectionPoolContext(), chunkSize: 1 * 1024, hardCap: 16);

        flow.InitStreamSendWindow(1);
        var windowBefore = flow.ConnectionSendWindow;

        // Use a blocking stream that returns a Task (not ValueTask with sync result)
        // so the scheduler goes through the async/PipeTo path.
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var blockingStream = new DelegatingReadStream((_, _) => new ValueTask<int>(tcs.Task));
        scheduler.Register(1, blockingStream, null, CancellationToken.None);

        // Window should be reserved (decremented by chunkSize=1024)
        Assert.Equal(windowBefore - 1 * 1024, flow.ConnectionSendWindow);

        // Cancel the stream while read is in flight
        scheduler.Cancel(1);

        // Simulate the async PipeTo callback arriving after cancel
        tcs.SetResult(50);
        scheduler.HandleReadComplete(1, 50);

        // After HandleReadComplete on orphaned slot, full reservation must be refunded
        Assert.Equal(windowBefore, flow.ConnectionSendWindow);
    }

    [Fact(Timeout = 5000)]
    public void WindowBlocked_should_block_when_available_below_half_chunk()
    {
        var target = new FakeTarget();
        // chunkSize = 1024, minReadSize = 512
        // Set connection window to 256 (below minReadSize)
        var flow = MakeFlow(connWindow: 65535);
        var scheduler = new BodyDrainScheduler(target, flow, new CancellationTokenSource(), new ConnectionPoolContext(), chunkSize: 1 * 1024, hardCap: 16);

        flow.InitStreamSendWindow(1);
        // Leave stream window at 65535 but exhaust connection window to 256
        flow.OnDataSent(1, 65535 - 256);
        flow.OnSendWindowUpdate(0, -(65535 - 256));

        // Register: connWindow=256, streamWindow=256, available=256, minRead=512
        // available < minReadSize so it should be blocked
        scheduler.Register(1, MakeBody(100), 100, CancellationToken.None);

        Assert.Empty(target.Emitted);
        Assert.Empty(target.Completed);
    }
}
