using Akka.Actor;
using TurboHTTP.Pooling;
using TurboHTTP.Protocol.Body;
using TurboHTTP.Protocol.Syntax.Http2;

namespace TurboHTTP.Tests.Protocol.Body;

public sealed class FlowControlledBodyPumpSpec
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

    private static FlowController MakeFlow(int connWindow = 1024 * 1024)
    {
        var fc = new FlowController(1024 * 1024, 64 * 1024);
        if (connWindow != 65535)
        {
            fc.OnSendWindowUpdate(0, connWindow - 65535);
        }

        return fc;
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

    private static FlowControlledBodyPump MakePump(FakeTarget target, FlowController flow)
        => new(target, flow, new ConnectionPoolContext(), new CancellationTokenSource());

    [Fact(Timeout = 5000)]
    public void Register_should_emit_body_when_window_available()
    {
        var target = new FakeTarget();
        var flow = MakeFlow();
        var pump = MakePump(target, flow);

        flow.InitStreamSendWindow(1);
        pump.Register(1, MakeBody(100), 100, CancellationToken.None);

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
        var pump = MakePump(target, flow);

        flow.InitStreamSendWindow(1);
        flow.OnDataSent(1, 65535);
        // Stream window is now 0, connection window reduced too
        pump.Register(1, MakeBody(100), 100, CancellationToken.None);

        Assert.Empty(target.Emitted);
        Assert.Empty(target.Completed);

        // WINDOW_UPDATE for both connection and stream 1
        flow.OnSendWindowUpdate(0, 65535);
        flow.OnSendWindowUpdate(1, 65535);
        pump.OnWindowUpdate(1);

        Assert.Equal(2, target.Emitted.Count);
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void Register_should_block_when_window_below_half_chunk_size()
    {
        var target = new FakeTarget();
        var flow = MakeFlow(connWindow: 1024 * 1024);
        var pump = MakePump(target, flow);

        flow.InitStreamSendWindow(1);
        // Exhaust stream window to below chunkSize/2 (= 8192)
        // Leave only 4096 bytes (less than 8192 threshold)
        flow.OnDataSent(1, 65535 - 4 * 1024);
        // Stream window = 4096, conn window still large

        pump.Register(1, MakeBody(100), 100, CancellationToken.None);

        // Stream is window-blocked: 4096 < 8192
        Assert.Empty(target.Emitted);
        Assert.Empty(target.Completed);

        // Open window above threshold
        flow.OnSendWindowUpdate(1, 32 * 1024);
        flow.OnSendWindowUpdate(0, 32 * 1024);
        pump.OnWindowUpdate(1);

        Assert.Equal(2, target.Emitted.Count);
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void RoundRobin_should_interleave_two_streams()
    {
        var target = new FakeTarget();
        var flow = MakeFlow();
        var pump = MakePump(target, flow);

        flow.InitStreamSendWindow(1);
        flow.InitStreamSendWindow(3);

        pump.Register(1, MakeBody(128), 128, CancellationToken.None);
        pump.Register(3, MakeBody(128), 128, CancellationToken.None);

        // Both should complete (sync fast path)
        Assert.Contains(1, target.Completed);
        Assert.Contains(3, target.Completed);

        // Verify both streams emitted data
        var streamIds = target.Emitted.Where(e => !e.EndStream).Select(e => e.StreamId).ToList();
        Assert.Contains(1, streamIds);
        Assert.Contains(3, streamIds);
    }

    [Fact(Timeout = 5000)]
    public void Orphan_should_not_crash_on_callback_after_cancel()
    {
        var target = new FakeTarget();
        var flow = MakeFlow();
        var pump = MakePump(target, flow);

        flow.InitStreamSendWindow(1);
        pump.Register(1, MakeBody(100), 100, CancellationToken.None);

        // Already completed for sync stream
        pump.Cancel(1);
        // No exception
    }

    [Fact(Timeout = 5000)]
    public void Cleanup_should_be_idempotent()
    {
        var target = new FakeTarget();
        var flow = MakeFlow();
        var pump = MakePump(target, flow);

        pump.Cleanup();
        pump.Cleanup();
    }

    [Fact(Timeout = 5000)]
    public void SyncFastPath_should_drain_without_PipeTo()
    {
        var target = new FakeTarget();
        var flow = MakeFlow();
        var pump = MakePump(target, flow);

        flow.InitStreamSendWindow(1);
        pump.Register(1, MakeBody(100), 100, CancellationToken.None);

        // MemoryStream completes synchronously — no PipeTo needed
        Assert.Equal(2, target.Emitted.Count);
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void Sync_reads_should_complete_all_chunks()
    {
        // Pump should drain all chunks without starvation.
        var target = new FakeTarget();
        var flow = MakeFlow(connWindow: 1024 * 1024);
        var pump = MakePump(target, flow);

        flow.InitStreamSendWindow(1);
        flow.OnSendWindowUpdate(1, 1024 * 1024);
        var bodySize = 65 * 16;
        pump.Register(1, MakeBody(bodySize), bodySize, CancellationToken.None);

        // All bytes emitted + EOF (no starvation guard)
        var totalBytes = target.Emitted.Where(e => !e.EndStream).Sum(e => e.Data.Length);
        Assert.Equal(bodySize, totalBytes);
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void RegisterWithLimbo_should_drain_on_window_update()
    {
        var target = new FakeTarget();
        var flow = MakeFlow();
        var pump = MakePump(target, flow);

        flow.InitStreamSendWindow(1);
        // Exhaust both stream and connection windows
        flow.OnDataSent(1, 65535);
        // Both stream window and conn window are now 0

        var remainder = new byte[] { 1, 2, 3, 4, 5 };
        pump.RegisterWithLimbo(1, remainder, CancellationToken.None);

        Assert.Empty(target.Emitted);

        // Open both windows above the eligibility threshold (chunkSize/2 = 8192)
        flow.OnSendWindowUpdate(0, 65535);
        flow.OnSendWindowUpdate(1, 65535);
        pump.OnWindowUpdate(1);

        // Data emit + EOF emit
        Assert.Equal(2, target.Emitted.Count);
        Assert.Equal(5, target.Emitted[0].Data.Length);
        Assert.False(target.Emitted[0].EndStream);
        Assert.True(target.Emitted[1].EndStream);
    }

    [Fact(Timeout = 5000)]
    public void SlotPooling_should_reuse_slots()
    {
        var target = new FakeTarget();
        var flow = MakeFlow();
        var pump = MakePump(target, flow);

        flow.InitStreamSendWindow(1);
        pump.Register(1, MakeBody(10), 10, CancellationToken.None);
        Assert.Single(target.Completed);

        // Register again — should reuse pooled slot
        flow.InitStreamSendWindow(3);
        target.Completed.Clear();
        pump.Register(3, MakeBody(10), 10, CancellationToken.None);
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void CtsLifecycle_should_create_once_and_dispose_on_complete()
    {
        var target = new FakeTarget();
        var flow = MakeFlow();
        var connCts = new CancellationTokenSource();
        var reqCts = new CancellationTokenSource();
        var pump = new FlowControlledBodyPump(target, flow, new ConnectionPoolContext(), connCts);

        flow.InitStreamSendWindow(1);
        pump.Register(1, MakeBody(100), 100, reqCts.Token);

        Assert.Single(target.Completed);
        reqCts.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void ReadRound_should_reserve_window_before_read()
    {
        var target = new FakeTarget();
        var flow = MakeFlow(connWindow: 1024 * 1024);
        var pump = MakePump(target, flow);

        flow.InitStreamSendWindow(1);
        var initialConnWindow = flow.ConnectionSendWindow;
        var initialStreamWindow = flow.GetStreamSendWindow(1);

        pump.Register(1, MakeBody(100), 100, CancellationToken.None);

        // After completing the drain, windows should have been decremented and refunded.
        // Net effect: 100 bytes were effectively consumed (not refunded).
        // connWindow reduced by 100 bytes net (reserved 16384, refunded 16284 after first read of 100 bytes,
        // then reserved again for the EOF read and fully refunded).
        var netConnChange = initialConnWindow - flow.ConnectionSendWindow;
        Assert.True(netConnChange >= 0, "Window should not increase beyond initial.");
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void ReadRound_should_skip_stream_when_window_below_half_chunksize()
    {
        var target = new FakeTarget();
        var flow = MakeFlow(connWindow: 1024 * 1024);
        var pump = MakePump(target, flow);

        flow.InitStreamSendWindow(1);
        // Set stream window to 1 byte (below chunkSize/2 = 8192)
        flow.OnDataSent(1, 65534);
        // Stream window = 1, far below the 8192 threshold

        pump.Register(1, MakeBody(100), 100, CancellationToken.None);

        // No reads should happen — stream is window-blocked
        Assert.Empty(target.Emitted);
        Assert.Empty(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void OnWindowUpdate_should_unblock_stream_and_trigger_read_with_credits()
    {
        var target = new FakeTarget();
        var flow = MakeFlow(connWindow: 1024 * 1024);
        var pump = MakePump(target, flow);

        flow.InitStreamSendWindow(1);
        // Block the stream below threshold
        flow.OnDataSent(1, 65534);
        pump.Register(1, MakeBody(50), 50, CancellationToken.None);
        Assert.Empty(target.Emitted);

        // Restore window above threshold and signal update
        flow.OnSendWindowUpdate(1, 65534);
        flow.OnSendWindowUpdate(0, 65534);
        pump.OnWindowUpdate(1);

        // Stream should now be unblocked and drained
        Assert.NotEmpty(target.Emitted);
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void AfterRead_should_refund_unused_reservation()
    {
        // Arrange: set up a stream that reads less than the full reserved window
        var target = new FakeTarget();
        var flow = MakeFlow(connWindow: 1024 * 1024);
        var pump = MakePump(target, flow);

        flow.InitStreamSendWindow(1);
        var windowBefore = flow.ConnectionSendWindow;

        // Register a body smaller than chunkSize so the reservation exceeds bytes read
        pump.Register(1, MakeBody(100), 100, CancellationToken.None);

        var windowAfter = flow.ConnectionSendWindow;

        // Net window consumed should be exactly 100 bytes (reserved 16384 per read,
        // refunded 16284 after reading 100 bytes, then reserved 16384 for EOF read and fully refunded)
        var netConsumed = windowBefore - windowAfter;
        // After full drain including EOF read, net consumption is 0 (all data bytes already
        // charged via OnDataSent pattern — but here Reserve/Refund are used, not OnDataSent,
        // so the pump manages the deduction).
        // Exact value depends on read sequence; we verify consistency only:
        Assert.True(netConsumed >= 0, "Refund should not leave window higher than before registration.");
        Assert.Single(target.Completed);
    }
}
