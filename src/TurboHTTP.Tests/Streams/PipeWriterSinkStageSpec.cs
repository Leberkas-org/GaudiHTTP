using System.IO.Pipelines;
using Akka.Streams.Dsl;
using TurboHTTP.Streams.Stages;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Streams;

public sealed class PipeWriterSinkStageSpec : StreamTestBase
{
    [Fact(Timeout = 5000)]
    public async Task Sink_should_write_data_to_pipe_reader()
    {
        var ct = TestContext.Current.CancellationToken;
        var pipe = new Pipe();
        var sink = Sink.FromGraph(new PipeWriterSinkStage(pipe.Writer));

        var data = new byte[] { 1, 2, 3, 4, 5 };
        await Source.Single((ReadOnlyMemory<byte>)data.AsMemory())
            .RunWith(sink, Materializer);

        var readResult = await pipe.Reader.ReadAsync(ct);
        Assert.Equal(data, readResult.Buffer.FirstSpan.ToArray());
        pipe.Reader.AdvanceTo(readResult.Buffer.End);

        var finalRead = await pipe.Reader.ReadAsync(ct);
        Assert.True(finalRead.IsCompleted);
        await pipe.Reader.CompleteAsync();
    }

    [Fact(Timeout = 5000)]
    public async Task Sink_should_write_multiple_chunks_to_pipe_reader()
    {
        var ct = TestContext.Current.CancellationToken;
        var pipe = new Pipe();
        var sink = Sink.FromGraph(new PipeWriterSinkStage(pipe.Writer));

        var chunks = new[]
        {
            new byte[] { 1, 2, 3 },
            new byte[] { 4, 5, 6 },
            new byte[] { 7, 8, 9 }
        };

        var writeTask = Source.From(chunks.Select(c => (ReadOnlyMemory<byte>)c.AsMemory()))
            .RunWith(sink, Materializer);

        var total = new List<byte>();
        while (true)
        {
            var readResult = await pipe.Reader.ReadAsync(ct);
            foreach (var segment in readResult.Buffer)
            {
                total.AddRange(segment.ToArray());
            }

            pipe.Reader.AdvanceTo(readResult.Buffer.End);

            if (readResult.IsCompleted)
            {
                break;
            }
        }

        await pipe.Reader.CompleteAsync();
        await writeTask;
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, total.ToArray());
    }

    [Fact(Timeout = 5000)]
    public async Task Sink_should_complete_task_when_upstream_finishes()
    {
        var ct = TestContext.Current.CancellationToken;
        var pipe = new Pipe();
        var sink = Sink.FromGraph(new PipeWriterSinkStage(pipe.Writer));

        var task = Source.Empty<ReadOnlyMemory<byte>>()
            .RunWith(sink, Materializer);

        await task;

        var readResult = await pipe.Reader.ReadAsync(ct);
        Assert.True(readResult.IsCompleted);
        Assert.True(readResult.Buffer.IsEmpty);
        await pipe.Reader.CompleteAsync();
    }

    [Fact(Timeout = 5000)]
    public async Task Sink_should_fault_task_when_upstream_fails()
    {
        var pipe = new Pipe();
        var sink = Sink.FromGraph(new PipeWriterSinkStage(pipe.Writer));

        var error = new InvalidOperationException("test failure");
        var task = Source.Failed<ReadOnlyMemory<byte>>(error)
            .RunWith(sink, Materializer);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        Assert.Equal("test failure", ex.Message);
        await pipe.Reader.CompleteAsync();
    }

    [Fact(Timeout = 5000)]
    public async Task Sink_should_skip_empty_chunks()
    {
        var ct = TestContext.Current.CancellationToken;
        var pipe = new Pipe();
        var sink = Sink.FromGraph(new PipeWriterSinkStage(pipe.Writer));

        var chunks = new[]
        {
            ReadOnlyMemory<byte>.Empty,
            (ReadOnlyMemory<byte>)new byte[] { 1, 2, 3 },
            ReadOnlyMemory<byte>.Empty
        };

        var writeTask = Source.From(chunks)
            .RunWith(sink, Materializer);

        var total = new List<byte>();
        while (true)
        {
            var readResult = await pipe.Reader.ReadAsync(ct);
            foreach (var segment in readResult.Buffer)
            {
                total.AddRange(segment.ToArray());
            }

            pipe.Reader.AdvanceTo(readResult.Buffer.End);

            if (readResult.IsCompleted)
            {
                break;
            }
        }

        await pipe.Reader.CompleteAsync();
        await writeTask;
        Assert.Equal(new byte[] { 1, 2, 3 }, total.ToArray());
    }
}