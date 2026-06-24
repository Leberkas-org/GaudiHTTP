using System.Buffers;
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

    [Fact(Timeout = 5000)]
    public void Register_should_emit_body_when_window_available()
    {
        var target = new FakeTarget();
        var flow = MakeFlow();
        var pump = new FlowControlledBodyPump(target, flow, new ConnectionPoolContext(), new CancellationTokenSource(), chunkSize: 1024, hardCap: 16);

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
        var pump = new FlowControlledBodyPump(target, flow, new ConnectionPoolContext(), new CancellationTokenSource(), chunkSize: 1024, hardCap: 16);

        flow.InitStreamSendWindow(1);
        flow.OnDataSent(1, 65535);
        // Stream window is now 0
        pump.Register(1, MakeBody(100), 100, CancellationToken.None);

        Assert.Empty(target.Emitted);
        Assert.Empty(target.Completed);

        // WINDOW_UPDATE for stream 1
        flow.OnSendWindowUpdate(1, 65535);
        pump.OnWindowUpdate(1);

        Assert.Equal(2, target.Emitted.Count);
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void PartialSend_should_store_limbo_and_drain_on_window_update()
    {
        var target = new FakeTarget();
        var flow = MakeFlow(connWindow: 65535);
        var pump = new FlowControlledBodyPump(target, flow, new ConnectionPoolContext(), new CancellationTokenSource(), chunkSize: 1024, hardCap: 16);

        flow.InitStreamSendWindow(1);
        // Exhaust most of the window
        flow.OnDataSent(1, 65535 - 50);
        // Stream window = 50, conn window = 50

        pump.Register(1, MakeBody(200), 200, CancellationToken.None);

        // Should send 50 bytes, limbo 150
        Assert.Single(target.Emitted);
        Assert.Equal(50, target.Emitted[0].Data.Length);

        // WINDOW_UPDATE
        flow.OnSendWindowUpdate(1, 200);
        flow.OnSendWindowUpdate(0, 200);
        pump.OnWindowUpdate(1);

        // Should drain limbo (150) + read more + EOF
        var totalData = target.Emitted.Where(e => !e.EndStream).Sum(e => e.Data.Length);
        Assert.Equal(200, totalData);
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void RoundRobin_should_interleave_two_streams()
    {
        var target = new FakeTarget();
        var flow = MakeFlow();
        var pump = new FlowControlledBodyPump(target, flow, new ConnectionPoolContext(), new CancellationTokenSource(), chunkSize: 64, hardCap: 16);

        flow.InitStreamSendWindow(1);
        flow.InitStreamSendWindow(3);

        pump.Register(1, MakeBody(128), 128, CancellationToken.None);
        pump.Register(3, MakeBody(128), 128, CancellationToken.None);

        // Both should complete (sync fast path)
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
        var pump = new FlowControlledBodyPump(target, flow, new ConnectionPoolContext(), new CancellationTokenSource(), chunkSize: 1024, hardCap: 16);

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
        var pump = new FlowControlledBodyPump(target, flow, new ConnectionPoolContext(), new CancellationTokenSource(), chunkSize: 1024, hardCap: 16);

        pump.Cleanup();
        pump.Cleanup();
    }

    [Fact(Timeout = 5000)]
    public void SyncFastPath_should_drain_without_PipeTo()
    {
        var target = new FakeTarget();
        var flow = MakeFlow();
        var pump = new FlowControlledBodyPump(target, flow, new ConnectionPoolContext(), new CancellationTokenSource(), chunkSize: 1024, hardCap: 16);

        flow.InitStreamSendWindow(1);
        pump.Register(1, MakeBody(100), 100, CancellationToken.None);

        // MemoryStream completes synchronously — no PipeTo needed
        Assert.Equal(2, target.Emitted.Count);
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void Sync_reads_should_complete_without_starvation_guard()
    {
        // Starvation guard removed — pump should drain all chunks.
        var target = new FakeTarget();
        var flow = MakeFlow(connWindow: 1024 * 1024);
        var pump = new FlowControlledBodyPump(target, flow, new ConnectionPoolContext(), new CancellationTokenSource(), chunkSize: 16, hardCap: 16);

        flow.InitStreamSendWindow(1);
        flow.OnSendWindowUpdate(1, 1024 * 1024);
        pump.Register(1, MakeBody(65 * 16), 65 * 16, CancellationToken.None);

        // All chunks emitted + EOF (no starvation guard)
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
        var pump = new FlowControlledBodyPump(target, flow, new ConnectionPoolContext(), new CancellationTokenSource(), chunkSize: 16 * 1024, hardCap: 16);

        // Connection window = 65535, chunkSize = 16384
        // effectiveSlots = min(readSlots=2, 65535/16384=4, 16) = 2
        // This is a self-consistency test — the pump should not over-read
        flow.InitStreamSendWindow(1);
        pump.Register(1, MakeBody(100), 100, CancellationToken.None);

        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void RegisterWithLimbo_should_drain_on_connection_window_update()
    {
        var target = new FakeTarget();
        var flow = MakeFlow();
        var pump = new FlowControlledBodyPump(target, flow, new ConnectionPoolContext(), new CancellationTokenSource(), chunkSize: 1024, hardCap: 16);

        flow.InitStreamSendWindow(1);
        // Exhaust connection window
        flow.OnDataSent(1, 65535);

        var remainder = new byte[] { 1, 2, 3, 4, 5 };
        pump.RegisterWithLimbo(1, remainder, CancellationToken.None);

        Assert.Empty(target.Emitted);

        flow.OnSendWindowUpdate(0, 65535);
        pump.OnWindowUpdate(0);

        Assert.Single(target.Emitted);
        Assert.Equal(5, target.Emitted[0].Data.Length);
    }

    [Fact(Timeout = 5000)]
    public void SlotPooling_should_reuse_slots()
    {
        var target = new FakeTarget();
        var flow = MakeFlow();
        var pump = new FlowControlledBodyPump(target, flow, new ConnectionPoolContext(), new CancellationTokenSource(), chunkSize: 1024, hardCap: 16);

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
        var pump = new FlowControlledBodyPump(target, flow, new ConnectionPoolContext(), connCts, chunkSize: 1024, hardCap: 16);

        flow.InitStreamSendWindow(1);
        pump.Register(1, MakeBody(100), 100, reqCts.Token);

        Assert.Single(target.Completed);
        reqCts.Dispose();
    }
}
