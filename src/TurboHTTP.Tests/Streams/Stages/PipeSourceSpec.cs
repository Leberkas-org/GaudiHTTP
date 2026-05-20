using TurboHTTP.Streams.Stages;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Streams.Stages;

public sealed class PipeSourceSpec : StreamTestBase
{
    [Fact(Timeout = 5000)]
    public async Task Source_should_emit_data_written_to_writer()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var pipeSource = new PipeSource();

        var resultTask = pipeSource.Source
            .RunAggregate(Array.Empty<byte>(), (acc, chunk) =>
            {
                var combined = new byte[acc.Length + chunk.Length];
                acc.CopyTo(combined, 0);
                chunk.Span.CopyTo(combined.AsSpan(acc.Length));
                return combined;
            }, Materializer);

        var memory = pipeSource.Writer.GetMemory(5);
        new byte[] { 1, 2, 3, 4, 5 }.CopyTo(memory);
        pipeSource.Writer.Advance(5);
        await pipeSource.Writer.FlushAsync(ct);
        await pipeSource.CompleteAsync();

        var result = await resultTask;
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Source_should_complete_when_writer_completes()
    {
        await using var pipeSource = new PipeSource();

        await pipeSource.CompleteAsync();

        var result = await pipeSource.Source
            .RunAggregate(0, (acc, chunk) => acc + chunk.Length, Materializer);

        Assert.Equal(0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Source_should_emit_multiple_chunks()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var pipeSource = new PipeSource();

        var countTask = pipeSource.Source
            .RunAggregate(0, (acc, chunk) => acc + chunk.Length, Materializer);

        for (var i = 0; i < 3; i++)
        {
            var mem = pipeSource.Writer.GetMemory(100);
            new byte[100].CopyTo(mem);
            pipeSource.Writer.Advance(100);
            await pipeSource.Writer.FlushAsync(ct);
        }

        await pipeSource.CompleteAsync();

        var total = await countTask;
        Assert.Equal(300, total);
    }

    [Fact(Timeout = 5000)]
    public async Task AsStream_should_write_to_source()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var pipeSource = new PipeSource();

        var resultTask = pipeSource.Source
            .RunAggregate(Array.Empty<byte>(), (acc, chunk) =>
            {
                var combined = new byte[acc.Length + chunk.Length];
                acc.CopyTo(combined, 0);
                chunk.Span.CopyTo(combined.AsSpan(acc.Length));
                return combined;
            }, Materializer);

        var stream = pipeSource.AsStream();
        await stream.WriteAsync(new byte[] { 10, 20, 30 }, ct);
        await stream.FlushAsync(ct);
        await pipeSource.CompleteAsync();

        var result = await resultTask;
        Assert.Equal(new byte[] { 10, 20, 30 }, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Dispose_should_complete_both_ends()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var pipeSource = new PipeSource();

        var resultTask = pipeSource.Source
            .RunAggregate(0, (acc, chunk) => acc + chunk.Length, Materializer);

        var mem = pipeSource.Writer.GetMemory(50);
        new byte[50].CopyTo(mem);
        pipeSource.Writer.Advance(50);
        await pipeSource.Writer.FlushAsync(ct);

        await pipeSource.CompleteAsync();

        var total = await resultTask;
        Assert.Equal(50, total);
    }
}
