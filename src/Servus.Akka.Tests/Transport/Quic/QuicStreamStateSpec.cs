using Servus.Akka.Transport;
using Servus.Akka.Transport.Quic;

namespace Servus.Akka.Tests.Transport.Quic;

[Collection("TransportBuffer")]
public sealed class QuicStreamStateSpec
{
    [Fact(Timeout = 5000)]
    public void New_state_should_be_Opening()
    {
        var state = new QuicStreamState(StreamDirection.Bidirectional);
        Assert.Equal(StreamPhase.Opening, state.Phase);
        Assert.False(state.HasHandle);
    }

    [Fact(Timeout = 5000)]
    public void Write_in_Opening_should_buffer()
    {
        var state = new QuicStreamState(StreamDirection.Bidirectional);
        var buf = TransportBuffer.Rent(2);
        buf.FullMemory.Span[0] = 0x01;
        buf.FullMemory.Span[1] = 0x02;
        buf.Length = 2;

        state.Write(buf);

        Assert.Equal(StreamPhase.Opening, state.Phase);
        Assert.Equal(1, state.PendingWriteCount);
    }

    [Fact(Timeout = 5000)]
    public void CompleteWrites_in_Opening_should_defer()
    {
        var state = new QuicStreamState(StreamDirection.Bidirectional);
        state.CompleteWrites();

        Assert.Equal(StreamPhase.Opening, state.Phase);
        Assert.True(state.IsCompleteWritesDeferred);
    }

    [Fact(Timeout = 5000)]
    public void AttachHandle_should_transition_to_Active()
    {
        var state = new QuicStreamState(StreamDirection.Bidirectional);
        var handle = new StreamHandle(new MemoryStream());

        state.AttachHandle(handle);

        Assert.Equal(StreamPhase.Active, state.Phase);
        Assert.True(state.HasHandle);
    }

    [Fact(Timeout = 5000)]
    public void AttachHandle_should_flush_pending_writes()
    {
        var state = new QuicStreamState(StreamDirection.Bidirectional);
        var buf = TransportBuffer.Rent(2);
        buf.FullMemory.Span[0] = 0x01;
        buf.FullMemory.Span[1] = 0x02;
        buf.Length = 2;
        state.Write(buf);

        var handle = new StreamHandle(new MemoryStream());
        state.AttachHandle(handle);

        Assert.Equal(0, state.PendingWriteCount);
    }

    [Fact(Timeout = 5000)]
    public void AttachHandle_with_deferred_CompleteWrites_should_transition_to_HalfClosedWrite()
    {
        var state = new QuicStreamState(StreamDirection.Bidirectional);
        state.CompleteWrites();

        state.AttachHandle(new StreamHandle(new MemoryStream()));

        Assert.Equal(StreamPhase.HalfClosedWrite, state.Phase);
    }

    [Fact(Timeout = 5000)]
    public void CompleteWrites_in_Active_should_transition_to_HalfClosedWrite()
    {
        var state = new QuicStreamState(StreamDirection.Bidirectional);
        state.AttachHandle(new StreamHandle(new MemoryStream()));

        state.CompleteWrites();

        Assert.Equal(StreamPhase.HalfClosedWrite, state.Phase);
    }

    [Fact(Timeout = 5000)]
    public void OnReadCompleted_in_HalfClosedWrite_should_transition_to_Closed()
    {
        var state = new QuicStreamState(StreamDirection.Bidirectional);
        state.AttachHandle(new StreamHandle(new MemoryStream()));
        state.CompleteWrites();

        state.OnReadCompleted();

        Assert.Equal(StreamPhase.Closed, state.Phase);
    }

    [Fact(Timeout = 5000)]
    public void OnReadCompleted_in_Active_should_transition_to_HalfClosedRead()
    {
        var state = new QuicStreamState(StreamDirection.Bidirectional);
        state.AttachHandle(new StreamHandle(new MemoryStream()));

        state.OnReadCompleted();

        Assert.Equal(StreamPhase.HalfClosedRead, state.Phase);
    }

    [Fact(Timeout = 5000)]
    public void Abort_should_transition_to_Closed()
    {
        var state = new QuicStreamState(StreamDirection.Bidirectional);
        state.AttachHandle(new StreamHandle(new MemoryStream()));

        state.Abort(0);

        Assert.Equal(StreamPhase.Closed, state.Phase);
    }
}
