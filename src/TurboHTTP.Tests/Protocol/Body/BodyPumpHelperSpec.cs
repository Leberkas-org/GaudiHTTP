using Akka.Actor;
using TurboHTTP.Protocol.Body;

namespace TurboHTTP.Tests.Protocol.Body;

public sealed class BodyPumpHelperSpec
{
    private static BodyDrainSlot<int> MakeSlot(Stream bodyStream, int chunkSize = 64)
    {
        var slot = new BodyDrainSlot<int>();
        slot.Initialize(1, bodyStream, null, CancellationToken.None, null);
        slot.EnsureBuffer(chunkSize);
        return slot;
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
    public void StartRead_should_return_CompletedSynchronously_for_sync_stream()
    {
        var body = MakeBody(100);
        var slot = MakeSlot(body, 256);

        var result = BodyPumpHelper.StartRead(slot, 256, ActorRefs.Nobody);

        Assert.Equal(BodyPumpHelper.ReadOutcome.CompletedSynchronously, result.Outcome);
        Assert.Equal(100, result.BytesRead);
        Assert.False(slot.IsReadInFlight);
        Assert.Equal(1, slot.ConsecutiveSyncReads);

        slot.DisposeResources();
    }

    [Fact(Timeout = 5000)]
    public void StartRead_should_return_CompletedSynchronously_with_zero_for_EOF()
    {
        var body = new MemoryStream([]);
        var slot = MakeSlot(body, 64);

        var result = BodyPumpHelper.StartRead(slot, 64, ActorRefs.Nobody);

        Assert.Equal(BodyPumpHelper.ReadOutcome.CompletedSynchronously, result.Outcome);
        Assert.Equal(0, result.BytesRead);

        slot.DisposeResources();
    }

    [Fact(Timeout = 5000)]
    public void StartRead_should_return_Dispatched_for_async_stream()
    {
        // Use a never-completing stream to force the async path
        var neverStream = new NeverCompletingStream();
        var slot = new BodyDrainSlot<int>();
        slot.Initialize(2, neverStream, null, CancellationToken.None, null);
        slot.EnsureBuffer(64);

        var result = BodyPumpHelper.StartRead(slot, 64, ActorRefs.Nobody);

        Assert.Equal(BodyPumpHelper.ReadOutcome.Dispatched, result.Outcome);
        Assert.Equal(0, result.BytesRead);
        // IsReadInFlight stays true — BeginRead() was called but not cleared (async path)
        Assert.True(slot.IsReadInFlight);

        slot.DisposeResources();
    }

    /// <summary>Stream whose ReadAsync never completes synchronously.</summary>
    private sealed class NeverCompletingStream : Stream
    {
        private readonly TaskCompletionSource<int> _tcs = new();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => new(_tcs.Task);
    }

    [Fact(Timeout = 5000)]
    public void StartRead_should_return_YieldedForStarvation_after_max_sync_reads()
    {
        // A MemoryStream with enough data for >64 sync reads of 16 bytes each
        var body = MakeBody(BodyPumpHelper.MaxSyncReadsPerDispatch * 16 + 16);
        var slot = MakeSlot(body, 16);

        var yieldCount = 0;
        var syncCount = 0;

        for (var i = 0; i < BodyPumpHelper.MaxSyncReadsPerDispatch + 1; i++)
        {
            var result = BodyPumpHelper.StartRead(slot, 16, ActorRefs.Nobody);

            if (result.Outcome == BodyPumpHelper.ReadOutcome.YieldedForStarvation)
            {
                yieldCount++;
                break;
            }

            Assert.Equal(BodyPumpHelper.ReadOutcome.CompletedSynchronously, result.Outcome);
            syncCount++;
        }

        Assert.Equal(1, yieldCount);
        // The yield fires on the MaxSyncReadsPerDispatch-th sync completion, so
        // syncCount captures the completions before that call's yield return.
        Assert.Equal(BodyPumpHelper.MaxSyncReadsPerDispatch - 1, syncCount);
        // After yield: counter was reset
        Assert.Equal(0, slot.ConsecutiveSyncReads);

        slot.DisposeResources();
    }

    [Fact(Timeout = 5000)]
    public void StartRead_should_reset_sync_counter_after_yield()
    {
        var body = MakeBody((BodyPumpHelper.MaxSyncReadsPerDispatch + 1) * 16);
        var slot = MakeSlot(body, 16);

        // Drive to starvation yield
        BodyPumpHelper.ReadResult result;
        do
        {
            result = BodyPumpHelper.StartRead(slot, 16, ActorRefs.Nobody);
        } while (result.Outcome == BodyPumpHelper.ReadOutcome.CompletedSynchronously);

        Assert.Equal(BodyPumpHelper.ReadOutcome.YieldedForStarvation, result.Outcome);
        Assert.Equal(0, slot.ConsecutiveSyncReads);

        // Simulate DrainContinue: clear in-flight marker and resume
        slot.CompleteAsyncRead();

        var next = BodyPumpHelper.StartRead(slot, 16, ActorRefs.Nobody);
        Assert.Equal(BodyPumpHelper.ReadOutcome.CompletedSynchronously, next.Outcome);
        Assert.Equal(1, slot.ConsecutiveSyncReads);

        slot.DisposeResources();
    }
}
