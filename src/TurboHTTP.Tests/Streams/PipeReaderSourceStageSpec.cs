using System.IO.Pipelines;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Streams.Shared;

namespace TurboHTTP.Tests.Streams;

public sealed class PipeReaderSourceStageSpec : IDisposable
{
    private readonly ActorSystem _system;
    private readonly IMaterializer _materializer;

    public PipeReaderSourceStageSpec()
    {
        _system = ActorSystem.Create("test");
        _materializer = _system.Materializer();
    }

    [Fact(Timeout = 5000)]
    public async Task Source_should_emit_data_written_to_pipe()
    {
        var pipe = new Pipe();
        var source = Source.FromGraph(new PipeReaderSourceStage(pipe.Reader));

        var resultTask = source
            .RunAggregate(Array.Empty<byte>(), (acc, chunk) =>
            {
                var combined = new byte[acc.Length + chunk.Length];
                acc.CopyTo(combined, 0);
                chunk.Span.CopyTo(combined.AsSpan(acc.Length));
                return combined;
            }, _materializer);

        var writer = pipe.Writer;
        var memory = writer.GetMemory(5);
        new byte[] { 1, 2, 3, 4, 5 }.CopyTo(memory);
        writer.Advance(5);
        await writer.FlushAsync(TestContext.Current.CancellationToken);
        await writer.CompleteAsync();

        var result = await resultTask;
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Source_should_complete_when_pipe_writer_completes()
    {
        var pipe = new Pipe();
        var source = Source.FromGraph(new PipeReaderSourceStage(pipe.Reader));

        await pipe.Writer.CompleteAsync();

        var result = await source
            .RunAggregate(0, (acc, chunk) => acc + chunk.Length, _materializer);

        Assert.Equal(0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Source_should_emit_multiple_chunks()
    {
        var pipe = new Pipe();
        var source = Source.FromGraph(new PipeReaderSourceStage(pipe.Reader));

        var countTask = source
            .RunAggregate(0, (acc, chunk) => acc + chunk.Length, _materializer);

        var writer = pipe.Writer;
        for (var i = 0; i < 3; i++)
        {
            var mem = writer.GetMemory(100);
            new byte[100].CopyTo(mem);
            writer.Advance(100);
            await writer.FlushAsync(TestContext.Current.CancellationToken);
        }

        await writer.CompleteAsync();

        var total = await countTask;
        Assert.Equal(300, total);
    }

    public void Dispose()
    {
        _system.Terminate().Wait(TimeSpan.FromSeconds(5));
    }
}
