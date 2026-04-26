using System.IO.Pipelines;
using Servus.Akka.IO;
using Servus.Akka.IO.Quic;

namespace Servus.Akka.Tests.IO;

public sealed class ClientStateSpec
{
    [Fact(Timeout = 5000)]
    public void ClientState_should_dispose_stream_on_dispose()
    {
        var stream = new MemoryStream();
        var state = new ClientState(stream);

        state.Dispose();

        Assert.Throws<ObjectDisposedException>(() => stream.ReadByte());
    }

    [Fact(Timeout = 5000)]
    public void ClientState_should_create_pipes_by_default()
    {
        var stream = new MemoryStream();
        var state = new ClientState(stream);

        Assert.NotNull(state.InboundPipe);
        Assert.NotNull(state.OutboundPipe);
    }

    [Fact(Timeout = 5000)]
    public async Task ClientState_should_have_working_inbound_pipe()
    {
        var stream = new MemoryStream();
        var state = new ClientState(stream);

        // Verify pipe can be written to and read from
        var writer = state.InboundPipe.Writer;
        var data = new byte[] { 1, 2, 3 };
        await writer.WriteAsync(data);
        writer.Complete();

        var result = await state.InboundPipe.Reader.ReadAsync();
        Assert.Equal(3, result.Buffer.Length);
        state.InboundPipe.Reader.AdvanceTo(result.Buffer.End);
        state.InboundPipe.Reader.Complete();
    }

    [Fact(Timeout = 5000)]
    public async Task ClientState_should_have_working_outbound_pipe()
    {
        var stream = new MemoryStream();
        var state = new ClientState(stream);

        // Verify pipe can be written to and read from
        var writer = state.OutboundPipe.Writer;
        var data = new byte[] { 4, 5, 6 };
        await writer.WriteAsync(data);
        writer.Complete();

        var result = await state.OutboundPipe.Reader.ReadAsync();
        Assert.Equal(3, result.Buffer.Length);
        state.OutboundPipe.Reader.AdvanceTo(result.Buffer.End);
        state.OutboundPipe.Reader.Complete();
    }

    [Fact(Timeout = 5000)]
    public void ClientState_should_expose_stream_property()
    {
        var stream = new MemoryStream();
        var state = new ClientState(stream);

        Assert.Same(stream, state.Stream);
    }

    [Fact(Timeout = 5000)]
    public void ClientState_should_allow_on_writes_complete_callback()
    {
        var stream = new MemoryStream();
        var state = new ClientState(stream)
        {
            OnWritesComplete = () => { }
        };

        Assert.NotNull(state.OnWritesComplete);
    }

    [Fact(Timeout = 5000)]
    public void ClientState_should_complete_pipes_on_dispose()
    {
        var stream = new MemoryStream();
        var state = new ClientState(stream);

        state.Dispose();

        // After dispose, pipes should be completed — writing should throw
        Assert.Throws<InvalidOperationException>(() =>
        {
            state.InboundPipe.Writer.GetMemory(1);
        });
    }

    [Fact(Timeout = 5000)]
    public void ClientState_should_handle_double_dispose()
    {
        var stream = new MemoryStream();
        var state = new ClientState(stream);

        state.Dispose();
        state.Dispose(); // Should not throw
    }

    [Fact(Timeout = 5000)]
    public void ClientState_should_create_with_write_only_direction()
    {
        var stream = new MemoryStream();
        var state = new ClientState(stream, StreamDirection.WriteOnly);

        Assert.Equal(StreamDirection.WriteOnly, state.Direction);
        Assert.NotNull(state.OutboundPipe);

        state.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void ClientState_should_create_with_read_only_direction()
    {
        var stream = new MemoryStream();
        var state = new ClientState(stream, StreamDirection.ReadOnly);

        Assert.Equal(StreamDirection.ReadOnly, state.Direction);
        Assert.NotNull(state.InboundPipe);

        state.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void ClientState_should_default_to_bidirectional_direction()
    {
        var stream = new MemoryStream();
        var state = new ClientState(stream);

        Assert.Equal(StreamDirection.Bidirectional, state.Direction);

        state.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void ClientState_should_expose_on_writes_complete_as_null_by_default()
    {
        var stream = new MemoryStream();
        var state = new ClientState(stream);

        Assert.Null(state.OnWritesComplete);

        state.Dispose();
    }
}
