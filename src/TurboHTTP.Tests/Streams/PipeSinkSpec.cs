using Akka.Streams.Dsl;
using TurboHTTP.Streams.Shared;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Streams;

public sealed class PipeSinkSpec : StreamTestBase
{
    [Fact(Timeout = 5000)]
    public async Task Sink_should_deliver_data_to_reader()
    {
        await using var pipeSink = new PipeSink();

        var data = new byte[] { 1, 2, 3, 4, 5 };
        var writeTask = Source.Single((ReadOnlyMemory<byte>)data.AsMemory())
            .RunWith(pipeSink.Sink, Materializer);

        var readResult = await pipeSink.Reader.ReadAsync();
        Assert.Equal(data, readResult.Buffer.FirstSpan.ToArray());
        pipeSink.Reader.AdvanceTo(readResult.Buffer.End);

        var finalRead = await pipeSink.Reader.ReadAsync();
        Assert.True(finalRead.IsCompleted);
        await pipeSink.Reader.CompleteAsync();
        await writeTask;
    }

    [Fact(Timeout = 5000)]
    public async Task Sink_should_deliver_multiple_chunks_to_reader()
    {
        await using var pipeSink = new PipeSink();

        var chunks = new[]
        {
            new byte[] { 1, 2, 3 },
            new byte[] { 4, 5, 6 },
            new byte[] { 7, 8, 9 }
        };

        var writeTask = Source.From(chunks.Select(c => (ReadOnlyMemory<byte>)c.AsMemory()))
            .RunWith(pipeSink.Sink, Materializer);

        var total = new List<byte>();
        while (true)
        {
            var readResult = await pipeSink.Reader.ReadAsync();
            foreach (var segment in readResult.Buffer)
            {
                total.AddRange(segment.ToArray());
            }

            pipeSink.Reader.AdvanceTo(readResult.Buffer.End);

            if (readResult.IsCompleted)
            {
                break;
            }
        }

        await pipeSink.Reader.CompleteAsync();
        await writeTask;
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, total.ToArray());
    }

    [Fact(Timeout = 5000)]
    public async Task Sink_should_complete_task_when_source_finishes()
    {
        await using var pipeSink = new PipeSink();

        var task = Source.Empty<ReadOnlyMemory<byte>>()
            .RunWith(pipeSink.Sink, Materializer);

        await task;

        var readResult = await pipeSink.Reader.ReadAsync();
        Assert.True(readResult.IsCompleted);
        Assert.True(readResult.Buffer.IsEmpty);
        await pipeSink.Reader.CompleteAsync();
    }

    [Fact(Timeout = 5000)]
    public async Task AsStream_should_read_from_sink()
    {
        await using var pipeSink = new PipeSink();

        var data = new byte[] { 10, 20, 30 };
        var writeTask = Source.Single((ReadOnlyMemory<byte>)data.AsMemory())
            .RunWith(pipeSink.Sink, Materializer);

        var stream = pipeSink.AsStream();
        var buffer = new byte[3];
        var bytesRead = await stream.ReadAsync(buffer);

        Assert.Equal(3, bytesRead);
        Assert.Equal(data, buffer);
        await writeTask;
    }

    [Fact(Timeout = 5000)]
    public async Task Dispose_should_complete_both_ends()
    {
        var pipeSink = new PipeSink();

        var task = Source.From(Enumerable.Range(0, 1000)
                .Select(i => (ReadOnlyMemory<byte>)new byte[] { (byte)i }.AsMemory()))
            .RunWith(pipeSink.Sink, Materializer);

        await pipeSink.DisposeAsync();

        await Assert.ThrowsAnyAsync<Exception>(() => task);
    }
}
