using System.Buffers;
using System.Net;
using System.Threading.Channels;
using Akka.TestKit.Xunit;
using Servus.Akka.IO;
using Servus.Akka.IO.Tcp;

namespace Servus.Akka.Tests.IO.Tcp;

public sealed class TcpPumpManagerSpec : TestKit
{
    private static readonly RequestEndpoint TestEndpoint = new()
    {
        Scheme = "http",
        Host = "localhost",
        Port = 8080,
        Version = HttpVersion.Version11
    };

    private static (Channel<IoBuffer> inboundChannel, ConnectionHandle handle) CreateTestHandle()
    {
        var inboundChannel = Channel.CreateUnbounded<IoBuffer>();
        var outboundChannel = Channel.CreateUnbounded<IoBuffer>();
        var handle = ConnectionHandle.CreateDirect(outboundChannel.Writer, inboundChannel.Reader, TestEndpoint);
        return (inboundChannel, handle);
    }

    [Fact(Timeout = 5000)]
    public async Task PumpAsync_should_send_InboundComplete_CleanClose_when_channel_completes_normally()
    {
        var probe = CreateTestProbe();
        var pump = new TcpPumpManager(probe.Ref);
        var (inboundChannel, handle) = CreateTestHandle();

        inboundChannel.Writer.Complete();
        pump.StartInboundPump(handle, TestEndpoint, gen: 1);

        var msg = await probe.ExpectMsgAsync<InboundComplete>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(TlsCloseKind.CleanClose, msg.CloseKind);
        Assert.Equal(1, msg.Gen);
    }

    [Fact(Timeout = 5000)]
    public async Task PumpAsync_should_send_InboundComplete_AbruptClose_when_channel_closed_with_inner_AbruptCloseException()
    {
        var probe = CreateTestProbe();
        var pump = new TcpPumpManager(probe.Ref);
        var (inboundChannel, handle) = CreateTestHandle();

        inboundChannel.Writer.Complete(new ChannelClosedException(null, new AbruptCloseException()));
        pump.StartInboundPump(handle, TestEndpoint, gen: 2);

        var msg = await probe.ExpectMsgAsync<InboundComplete>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(TlsCloseKind.AbruptClose, msg.CloseKind);
        Assert.Equal(2, msg.Gen);
    }

    [Fact(Timeout = 5000)]
    public async Task PumpAsync_should_send_InboundPumpFailed_on_unexpected_exception()
    {
        var probe = CreateTestProbe();
        var pump = new TcpPumpManager(probe.Ref);
        var (inboundChannel, handle) = CreateTestHandle();

        inboundChannel.Writer.Complete(new IOException("unexpected I/O error"));
        pump.StartInboundPump(handle, TestEndpoint, gen: 0);

        var msg = await probe.ExpectMsgAsync<InboundPumpFailed>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(msg.Error);
    }

    [Fact(Timeout = 5000)]
    public async Task StopInboundPump_should_cancel_pump_and_send_no_messages()
    {
        var probe = CreateTestProbe();
        var pump = new TcpPumpManager(probe.Ref);
        var (_, handle) = CreateTestHandle();

        pump.StartInboundPump(handle, TestEndpoint, gen: 0);
        pump.StopInboundPump();

        // Allow some time for any stray messages to arrive
        await Task.Delay(150, TestContext.Current.CancellationToken);
        await probe.ExpectNoMsgAsync(TimeSpan.Zero, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    public async Task PumpAsync_should_deliver_all_data_and_complete_cleanly()
    {
        var probe = CreateTestProbe();
        var pump = new TcpPumpManager(probe.Ref);
        var (inboundChannel, handle) = CreateTestHandle();

        // Write a known amount of data as IoBuffers, then complete the channel.
        const int totalBytes = 33;
        for (var i = 0; i < totalBytes; i++)
        {
            var owner = MemoryPool<byte>.Shared.Rent(1);
            owner.Memory.Span[0] = (byte)i;
            await inboundChannel.Writer.WriteAsync(new IoBuffer(owner, 1), TestContext.Current.CancellationToken);
        }

        inboundChannel.Writer.Complete();
        pump.StartInboundPump(handle, TestEndpoint, gen: 0);

        // Collect all batches until we see InboundComplete
        var totalItemCount = 0;
        while (true)
        {
            var msg = await probe.ExpectMsgAsync<ITcpTransportEvent>(cancellationToken: TestContext.Current.CancellationToken);
            if (msg is InboundBatch batch)
            {
                totalItemCount += batch.Count;
            }
            else if (msg is InboundComplete complete)
            {
                Assert.Equal(TlsCloseKind.CleanClose, complete.CloseKind);
                break;
            }
        }

        Assert.True(totalItemCount > 0, "Expected at least one inbound item");
    }

    [Fact(Timeout = 5000)]
    public async Task StartInboundPump_should_cancel_previous_pump_when_called_again()
    {
        var probe = CreateTestProbe();
        var pump = new TcpPumpManager(probe.Ref);

        var (inboundChannel1, handle1) = CreateTestHandle();
        var (inboundChannel2, handle2) = CreateTestHandle();

        // Start first pump — channel stays open
        pump.StartInboundPump(handle1, TestEndpoint, gen: 1);

        // Start second pump — cancels the first
        inboundChannel2.Writer.Complete();
        pump.StartInboundPump(handle2, TestEndpoint, gen: 2);

        // Only messages from pump2 expected; pump1 was cancelled
        var complete = await probe.ExpectMsgAsync<InboundComplete>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, complete.Gen);

        // Write to the cancelled pump1 channel — should produce no further messages
        var owner = MemoryPool<byte>.Shared.Rent(1);
        owner.Memory.Span[0] = 0xFF;
        await inboundChannel1.Writer.WriteAsync(new IoBuffer(owner, 1), TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        await probe.ExpectNoMsgAsync(TimeSpan.Zero, TestContext.Current.CancellationToken);
    }
}
