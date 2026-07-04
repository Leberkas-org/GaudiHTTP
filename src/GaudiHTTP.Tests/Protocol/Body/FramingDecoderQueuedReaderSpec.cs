using GaudiHTTP.Protocol.Body;

namespace GaudiHTTP.Tests.Protocol.Body;

public sealed class FramingDecoderQueuedReaderSpec
{
    [Fact(Timeout = 5000)]
    public async Task ContentLength_decoder_should_enqueue_body_into_queued_reader()
    {
        var framing = new ContentLengthFramingDecoder();
        framing.Reset(5);
        var reader = new QueuedBodyReader(capacity: 4);
        reader.Reset();

        var input = "hello"u8.ToArray().AsSpan();
        var result = framing.Decode(input, out var consumed);
        Assert.Equal(5, consumed);
        Assert.True(result.EndOfBody);
        Assert.True(reader.TryEnqueue(result.Body));
        reader.Complete();

        var readResult = await reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal("hello"u8.ToArray(), readResult.Memory.ToArray());
        reader.AdvanceTo();

        var endResult = await reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.True(endResult.IsCompleted);
    }

    [Fact(Timeout = 5000)]
    public async Task Chunked_decoder_should_enqueue_body_chunks()
    {
        var framing = new ChunkedFramingDecoder();
        framing.Reset(1 * 1024 * 1024, 256, 64 * 1024, 32 * 1024);
        var reader = new QueuedBodyReader(capacity: 4);
        reader.Reset();

        var chunk = "5\r\nhello\r\n"u8.ToArray().AsSpan();
        var result = framing.Decode(chunk, out _);
        Assert.False(result.EndOfBody);
        Assert.True(reader.TryEnqueue(result.Body));

        var readResult = await reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal("hello"u8.ToArray(), readResult.Memory.ToArray());
        reader.AdvanceTo();

        var terminator = "0\r\n\r\n"u8.ToArray().AsSpan();
        var result2 = framing.Decode(terminator, out _);
        Assert.True(result2.EndOfBody);
        reader.Complete();

        var endResult = await reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.True(endResult.IsCompleted);
    }

    [Fact(Timeout = 5000)]
    public async Task CloseDelimited_decoder_should_enqueue_and_complete_on_eof()
    {
        var framing = new CloseDelimitedFramingDecoder();
        framing.Reset(1 * 1024 * 1024);
        var reader = new QueuedBodyReader(capacity: 4);
        reader.Reset();

        var data = "some data"u8.ToArray().AsSpan();
        var result = framing.Decode(data, out _);
        Assert.True(reader.TryEnqueue(result.Body));

        Assert.True(framing.OnEof());
        reader.Complete();

        var readResult = await reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal("some data"u8.ToArray(), readResult.Memory.ToArray());
        reader.AdvanceTo();

        var endResult = await reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.True(endResult.IsCompleted);
    }
}
