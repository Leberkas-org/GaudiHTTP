using System.Buffers;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol;

public sealed class InMemoryTransportSpec
{
    [Fact(Timeout = 5000)]
    public void Feed_should_deliver_data_via_sync_ReadAsync()
    {
        var transport = new InMemoryTransport();
        var data = new byte[] { 1, 2, 3 };

        transport.Feed(data);
        var vt = transport.ReadAsync();

        Assert.True(vt.IsCompletedSuccessfully);
        var result = vt.Result;
        Assert.False(result.IsCompleted);
        Assert.Equal(data, result.Buffer.ToArray());
    }

    [Fact(Timeout = 5000)]
    public async Task Feed_should_deliver_data_to_pending_ReadAsync()
    {
        var transport = new InMemoryTransport();
        var data = new byte[] { 4, 5, 6 };

        var vt = transport.ReadAsync();
        Assert.False(vt.IsCompleted);

        transport.Feed(data);

        var result = await vt;
        Assert.False(result.IsCompleted);
        Assert.Equal(data, result.Buffer.ToArray());
    }

    [Fact(Timeout = 5000)]
    public void Feed_ReadOnlySequence_should_deliver_multi_segment_data()
    {
        var transport = new InMemoryTransport();
        var first = new byte[] { 1, 2 };
        var second = new byte[] { 3, 4 };
        var segment1 = new BufferSegment(first);
        var segment2 = segment1.Append(second);
        var seq = new ReadOnlySequence<byte>(segment1, 0, segment2, second.Length);

        transport.Feed(seq);
        var result = transport.ReadAsync().Result;

        Assert.Equal(4, result.Buffer.Length);
    }

    [Fact(Timeout = 5000)]
    public void Complete_should_signal_end_of_stream()
    {
        var transport = new InMemoryTransport();

        transport.Complete();
        var result = transport.ReadAsync().Result;

        Assert.True(result.IsCompleted);
        Assert.True(result.Buffer.IsEmpty);
    }

    [Fact(Timeout = 5000)]
    public void ReadCount_should_increment_per_call()
    {
        var transport = new InMemoryTransport();
        transport.Feed(new byte[] { 1 });
        transport.Feed(new byte[] { 2 });

        transport.ReadAsync();
        transport.ReadAsync();

        Assert.Equal(2, transport.ReadCount);
    }

    [Fact(Timeout = 5000)]
    public void AdvanceTo_should_increment_count()
    {
        var transport = new InMemoryTransport();
        transport.Feed(new byte[] { 1 });
        var result = transport.ReadAsync().Result;

        transport.AdvanceTo(result.Buffer.End);
        transport.AdvanceTo(result.Buffer.Start, result.Buffer.End);

        Assert.Equal(2, transport.AdvanceToCount);
    }

    [Fact(Timeout = 5000)]
    public void GetMemory_and_Advance_should_capture_written_bytes()
    {
        var transport = new InMemoryTransport();

        var mem = transport.GetMemory(4);
        mem.Span[0] = 0xAA;
        mem.Span[1] = 0xBB;
        transport.Advance(2);

        mem = transport.GetMemory(2);
        mem.Span[0] = 0xCC;
        transport.Advance(1);

        Assert.Equal(3, transport.WrittenCount);
        Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC }, transport.WrittenSpan.ToArray());
    }

    [Fact(Timeout = 5000)]
    public void TakeWrittenBytes_should_return_and_reset()
    {
        var transport = new InMemoryTransport();

        var mem = transport.GetMemory(2);
        mem.Span[0] = 1;
        mem.Span[1] = 2;
        transport.Advance(2);

        var taken = transport.TakeWrittenBytes();
        Assert.Equal(new byte[] { 1, 2 }, taken);
        Assert.Equal(0, transport.WrittenCount);

        mem = transport.GetMemory(1);
        mem.Span[0] = 3;
        transport.Advance(1);
        Assert.Equal(new byte[] { 3 }, transport.TakeWrittenBytes());
    }

    [Fact(Timeout = 5000)]
    public void FlushAsync_Sync_should_return_completed()
    {
        var transport = new InMemoryTransport { FlushMode = Shared.FlushMode.Sync };

        var vt = transport.FlushAsync();

        Assert.True(vt.IsCompletedSuccessfully);
        Assert.False(vt.Result.IsCompleted);
        Assert.Equal(1, transport.FlushCount);
    }

    [Fact(Timeout = 5000)]
    public void FlushAsync_SyncCompleted_should_signal_pipe_closed()
    {
        var transport = new InMemoryTransport { FlushMode = Shared.FlushMode.SyncCompleted };

        var vt = transport.FlushAsync();

        Assert.True(vt.IsCompletedSuccessfully);
        Assert.True(vt.Result.IsCompleted);
    }

    [Fact(Timeout = 5000)]
    public void FlushAsync_Async_should_return_pending()
    {
        var transport = new InMemoryTransport { FlushMode = Shared.FlushMode.Async };

        var vt = transport.FlushAsync();

        Assert.False(vt.IsCompleted);
    }

    [Fact(Timeout = 5000)]
    public void CompleteOutput_should_set_flag()
    {
        var transport = new InMemoryTransport();

        Assert.False(transport.OutputCompleted);
        transport.CompleteOutput();
        Assert.True(transport.OutputCompleted);
    }

    [Fact(Timeout = 5000)]
    public void Abort_should_set_flag()
    {
        var transport = new InMemoryTransport();

        Assert.False(transport.Aborted);
        transport.Abort();
        Assert.True(transport.Aborted);
    }

    private sealed class BufferSegment : ReadOnlySequenceSegment<byte>
    {
        public BufferSegment(byte[] data)
        {
            Memory = data;
        }

        public BufferSegment Append(byte[] data)
        {
            var next = new BufferSegment(data) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;
            return next;
        }
    }
}
