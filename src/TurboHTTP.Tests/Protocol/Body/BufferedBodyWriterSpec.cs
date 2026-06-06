using System.Buffers;
using TurboHTTP.Protocol.Body;

namespace TurboHTTP.Tests.Protocol.Body;

public sealed class BufferedBodyWriterSpec
{
    [Fact(Timeout = 5000)]
    public async Task CompleteAsync_should_send_accumulated_data_via_callback()
    {
        IMemoryOwner<byte>? sentOwner = null;
        var sentLength = 0;

        using var writer = new BufferedBodyWriter();
        writer.Reset((owner, length) =>
        {
            sentOwner = owner;
            sentLength = length;
        });

        var mem = writer.GetMemory(5);
        "hello"u8.CopyTo(mem.Span);
        writer.Advance(5);

        await writer.CompleteAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(sentOwner);
        Assert.Equal(5, sentLength);
        Assert.Equal("hello"u8.ToArray(), sentOwner!.Memory[..sentLength].ToArray());
        sentOwner.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task FlushAsync_should_be_noop_for_buffered_writer()
    {
        using var writer = new BufferedBodyWriter();
        writer.Reset((_, _) => { });

        var mem = writer.GetMemory(3);
        "abc"u8.CopyTo(mem.Span);
        writer.Advance(3);

        var result = await writer.FlushAsync(TestContext.Current.CancellationToken);
        Assert.False(result.IsCompleted);
    }

    [Fact(Timeout = 5000)]
    public async Task GetMemory_should_grow_buffer_when_needed()
    {
        IMemoryOwner<byte>? sentOwner = null;
        var sentLength = 0;

        using var writer = new BufferedBodyWriter();
        writer.Reset((owner, length) =>
        {
            sentOwner = owner;
            sentLength = length;
        });

        for (var i = 0; i < 100; i++)
        {
            var mem = writer.GetMemory(64);
            var data = new byte[64];
            Array.Fill(data, (byte)(i % 256));
            data.CopyTo(mem);
            writer.Advance(64);
        }

        await writer.CompleteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(6400, sentLength);
        sentOwner?.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task Reset_should_allow_reuse()
    {
        var callCount = 0;

        using var writer = new BufferedBodyWriter();

        writer.Reset((owner, _) => { callCount++; owner.Dispose(); });
        var m1 = writer.GetMemory(3);
        "abc"u8.CopyTo(m1.Span);
        writer.Advance(3);
        await writer.CompleteAsync(TestContext.Current.CancellationToken);

        writer.Reset((owner, _) => { callCount++; owner.Dispose(); });
        var m2 = writer.GetMemory(2);
        "xy"u8.CopyTo(m2.Span);
        writer.Advance(2);
        await writer.CompleteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, callCount);
    }
}
