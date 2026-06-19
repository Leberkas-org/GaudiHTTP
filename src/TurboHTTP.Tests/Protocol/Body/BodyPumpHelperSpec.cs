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
    public void StartRead_should_increment_sync_counter_on_each_sync_read()
    {
        // StartRead increments ConsecutiveSyncReads on each sync completion.
        // The caller (pump) checks the counter before calling StartRead and yields
        // when it reaches MaxSyncReadsPerDispatch.
        var body = MakeBody(BodyPumpHelper.MaxSyncReadsPerDispatch * 16 + 16);
        var slot = MakeSlot(body, 16);

        for (var i = 0; i < BodyPumpHelper.MaxSyncReadsPerDispatch; i++)
        {
            var result = BodyPumpHelper.StartRead(slot, 16, ActorRefs.Nobody);
            Assert.Equal(BodyPumpHelper.ReadOutcome.CompletedSynchronously, result.Outcome);
            Assert.Equal(16, result.BytesRead);
            Assert.Equal(i + 1, slot.ConsecutiveSyncReads);
        }

        // After MaxSyncReadsPerDispatch reads, counter equals the threshold.
        // Caller should now yield instead of calling StartRead again.
        Assert.Equal(BodyPumpHelper.MaxSyncReadsPerDispatch, slot.ConsecutiveSyncReads);

        slot.DisposeResources();
    }

    [Fact(Timeout = 5000)]
    public void StartRead_should_resume_cleanly_after_counter_reset()
    {
        var body = MakeBody((BodyPumpHelper.MaxSyncReadsPerDispatch + 1) * 16);
        var slot = MakeSlot(body, 16);

        // Drive to threshold
        for (var i = 0; i < BodyPumpHelper.MaxSyncReadsPerDispatch; i++)
        {
            BodyPumpHelper.StartRead(slot, 16, ActorRefs.Nobody);
        }

        Assert.Equal(BodyPumpHelper.MaxSyncReadsPerDispatch, slot.ConsecutiveSyncReads);

        // Simulate what the pump does on yield: reset counter, then resume
        slot.ResetSyncReads();
        Assert.Equal(0, slot.ConsecutiveSyncReads);

        var next = BodyPumpHelper.StartRead(slot, 16, ActorRefs.Nobody);
        Assert.Equal(BodyPumpHelper.ReadOutcome.CompletedSynchronously, next.Outcome);
        Assert.Equal(1, slot.ConsecutiveSyncReads);

        slot.DisposeResources();
    }
}
