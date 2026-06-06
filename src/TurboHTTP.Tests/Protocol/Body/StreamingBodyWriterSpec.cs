using System.Buffers;
using TurboHTTP.Protocol.Body;

namespace TurboHTTP.Tests.Protocol.Body;

public sealed class StreamingBodyWriterSpec
{
    [Fact(Timeout = 5000)]
    public async Task FlushAsync_should_transfer_ownership_to_send_callback()
    {
        IMemoryOwner<byte>? receivedOwner = null;
        ReadOnlyMemory<byte> receivedData = default;

        var encoder = new PassthroughFramingEncoder();
        var writer = new StreamingBodyWriter();
        writer.Reset(encoder, (owner, data) =>
        {
            receivedOwner = owner;
            receivedData = data;
            return default;
        });

        var mem = writer.GetMemory(4);
        "test"u8.CopyTo(mem.Span);
        writer.Advance(4);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(receivedOwner);
        Assert.Equal(4, receivedData.Length);
        Assert.Equal("test"u8.ToArray(), receivedData.ToArray());

        receivedOwner.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task FlushAsync_should_not_dispose_rental_after_send()
    {
        IMemoryOwner<byte>? receivedOwner = null;

        var encoder = new PassthroughFramingEncoder();
        var writer = new StreamingBodyWriter();
        writer.Reset(encoder, (owner, data) =>
        {
            receivedOwner = owner;
            return default;
        });

        var mem = writer.GetMemory(4);
        "data"u8.CopyTo(mem.Span);
        writer.Advance(4);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        // Writer should NOT have disposed — caller owns it
        Assert.Equal((byte)'d', receivedOwner!.Memory.Span[0]);

        receivedOwner.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task CompleteAsync_should_send_terminator_with_ownership()
    {
        IMemoryOwner<byte>? terminatorOwner = null;

        var encoder = new ChunkedFramingEncoder(16 * 1024);
        var writer = new StreamingBodyWriter();
        writer.Reset(encoder, (owner, data) =>
        {
            terminatorOwner = owner;
            return default;
        });

        await writer.CompleteAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(terminatorOwner);
        terminatorOwner.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task FlushAsync_should_return_completed_false()
    {
        var encoder = new PassthroughFramingEncoder();
        var writer = new StreamingBodyWriter();
        writer.Reset(encoder, (owner, data) =>
        {
            owner.Dispose();
            return default;
        });

        var mem = writer.GetMemory(2);
        "ab"u8.CopyTo(mem.Span);
        writer.Advance(2);
        var result = await writer.FlushAsync(TestContext.Current.CancellationToken);

        Assert.False(result.IsCompleted);
    }

    [Fact(Timeout = 5000)]
    public async Task GetMemory_should_include_headroom_for_chunked_framing()
    {
        ReadOnlyMemory<byte> sentData = default;

        var encoder = new ChunkedFramingEncoder(4 * 1024);
        var writer = new StreamingBodyWriter();
        writer.Reset(encoder, (owner, data) =>
        {
            sentData = data;
            owner.Dispose();
            return default;
        });

        var mem = writer.GetMemory(3);
        Assert.True(mem.Length >= 3);
        "abc"u8.CopyTo(mem.Span);
        writer.Advance(3);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        var text = System.Text.Encoding.ASCII.GetString(sentData.Span);
        Assert.Contains("3\r\nabc\r\n", text);
    }

    [Fact(Timeout = 5000)]
    public async Task Reset_should_allow_reuse()
    {
        var callCount = 0;
        var encoder = new PassthroughFramingEncoder();
        using var writer = new StreamingBodyWriter();

        writer.Reset(encoder, (owner, _) => { callCount++; owner.Dispose(); return default; });
        var m1 = writer.GetMemory(2);
        "ab"u8.CopyTo(m1.Span);
        writer.Advance(2);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        writer.Reset(encoder, (owner, _) => { callCount++; owner.Dispose(); return default; });
        var m2 = writer.GetMemory(2);
        "cd"u8.CopyTo(m2.Span);
        writer.Advance(2);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, callCount);
    }
}
