using System.Text;
using TurboHTTP.Protocol.Body;

namespace TurboHTTP.Tests.Protocol.Body;

public sealed class BodyDecoderBridgeSpec
{
    [Fact(Timeout = 5000)]
    public async Task FeedStreamed_should_pass_input_memory_directly_for_zero_copy_decoder()
    {
        var framing = new ContentLengthFramingDecoder();
        framing.Reset(5);
        var reader = new BridgedBodyReader();
        reader.Reset();
        var bridge = new BodyDecoderBridge(framing, reader);

        var input = "hello"u8.ToArray().AsMemory();
        var disposed = false;
        var result = bridge.FeedStreamed(input, () => disposed = true);

        Assert.Equal(5, result.RawConsumed);
        Assert.True(result.IsComplete);

        var readResult = await reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.True(input.Span.Overlaps(readResult.Memory.Span));

        reader.AdvanceTo(readResult.Memory.Length);
        Assert.True(disposed);
    }

    [Fact(Timeout = 5000)]
    public async Task FeedStreamed_should_copy_body_for_non_zero_copy_decoder()
    {
        var framing = new ChunkedFramingDecoder();
        framing.Reset(1 * 1024 * 1024, 256);
        var reader = new BridgedBodyReader();
        reader.Reset();
        var bridge = new BodyDecoderBridge(framing, reader);

        var chunk = Encoding.ASCII.GetBytes("5\r\nhello\r\n").AsMemory();
        var result = bridge.FeedStreamed(chunk, () => { });
        Assert.False(result.IsComplete);

        var readResult = await reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal("hello"u8.ToArray(), readResult.Memory.ToArray());
        Assert.False(chunk.Span.Overlaps(readResult.Memory.Span));

        reader.AdvanceTo(readResult.Memory.Length);
    }

    [Fact(Timeout = 5000)]
    public async Task FeedStreamed_should_defer_complete_until_consumed_when_body_and_end()
    {
        var framing = new ContentLengthFramingDecoder();
        framing.Reset(5);
        var reader = new BridgedBodyReader();
        reader.Reset();
        var bridge = new BodyDecoderBridge(framing, reader);

        var input = "hello"u8.ToArray().AsMemory();
        var result = bridge.FeedStreamed(input, () => { });

        Assert.True(result.IsComplete);
        Assert.False(reader.IsCompleted);

        var readResult = await reader.ReadAsync(TestContext.Current.CancellationToken);
        reader.AdvanceTo(readResult.Memory.Length);

        Assert.True(reader.IsCompleted);
    }

    [Fact(Timeout = 5000)]
    public void FeedStreamed_should_handle_partial_content_length_body()
    {
        var framing = new ContentLengthFramingDecoder();
        framing.Reset(10);
        var reader = new BridgedBodyReader();
        reader.Reset();
        var bridge = new BodyDecoderBridge(framing, reader);

        var input = "hello"u8.ToArray().AsMemory();
        var result = bridge.FeedStreamed(input, () => { });

        Assert.Equal(5, result.RawConsumed);
        Assert.False(result.IsComplete);
        Assert.False(reader.IsCompleted);
    }

    [Fact(Timeout = 5000)]
    public async Task SignalEof_should_complete_reader_for_close_delimited()
    {
        var framing = new CloseDelimitedFramingDecoder();
        framing.Reset(1 * 1024 * 1024);
        var reader = new BridgedBodyReader();
        reader.Reset();
        var bridge = new BodyDecoderBridge(framing, reader);

        var input = "some data"u8.ToArray().AsMemory();
        bridge.FeedStreamed(input, () => { });

        var readResult = await reader.ReadAsync(TestContext.Current.CancellationToken);
        reader.AdvanceTo(readResult.Memory.Length);

        Assert.True(bridge.SignalEof());
        Assert.True(reader.IsCompleted);
    }

    [Fact(Timeout = 5000)]
    public async Task FeedStreamed_should_complete_reader_after_chunked_terminator()
    {
        var framing = new ChunkedFramingDecoder();
        framing.Reset(1 * 1024 * 1024, 256);
        var reader = new BridgedBodyReader();
        reader.Reset();
        var bridge = new BodyDecoderBridge(framing, reader);

        var chunk = Encoding.ASCII.GetBytes("5\r\nhello\r\n").AsMemory();
        bridge.FeedStreamed(chunk, () => { });

        var readResult = await reader.ReadAsync(TestContext.Current.CancellationToken);
        reader.AdvanceTo(readResult.Memory.Length);

        var terminator = Encoding.ASCII.GetBytes("0\r\n\r\n").AsMemory();
        var result2 = bridge.FeedStreamed(terminator, () => { });
        Assert.True(result2.IsComplete);
        Assert.True(reader.IsCompleted);
    }
}
