using System.IO.Pipelines;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit.Xunit;
using Servus.Akka.Streams.IO;

namespace Servus.Akka.Tests.Streams.IO;

public sealed class PipeSourceStageSpec : TestKit
{
    private readonly IMaterializer _materializer;

    public PipeSourceStageSpec() : base(ActorSystem.Create("test"))
    {
        _materializer = Sys.Materializer();
    }

    [Fact(Timeout = 5000)]
    public async Task Source_should_emit_data_written_to_pipe()
    {
        var pipe = new Pipe();
        var source = PipeSource.From(pipe.Reader);

        var data = new byte[] { 1, 2, 3, 4, 5 };
        await pipe.Writer.WriteAsync(data, CancellationToken.None);
        await pipe.Writer.CompleteAsync();

        var result = await source
            .RunWith(Sink.Seq<ReadOnlyMemory<byte>>(), _materializer);

        var combined = result.SelectMany(m => m.ToArray()).ToArray();
        Assert.Equal(data, combined);
    }

    [Fact(Timeout = 5000)]
    public async Task Source_should_complete_on_empty_pipe()
    {
        var pipe = new Pipe();
        await pipe.Writer.CompleteAsync();

        var source = PipeSource.From(pipe.Reader);

        var result = await source
            .RunWith(Sink.Seq<ReadOnlyMemory<byte>>(), _materializer);

        Assert.Empty(result);
    }

    [Fact(Timeout = 5000)]
    public async Task Source_should_emit_multiple_chunks()
    {
        var pipe = new Pipe();
        var source = PipeSource.From(pipe.Reader);

        await pipe.Writer.WriteAsync(new byte[] { 1, 2, 3 }, CancellationToken.None);
        await pipe.Writer.FlushAsync(CancellationToken.None);
        await pipe.Writer.WriteAsync(new byte[] { 4, 5, 6 }, CancellationToken.None);
        await pipe.Writer.CompleteAsync();

        var result = await source
            .RunWith(Sink.Seq<ReadOnlyMemory<byte>>(), _materializer);

        var combined = result.SelectMany(m => m.ToArray()).ToArray();
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, combined);
    }

    [Fact(Timeout = 5000)]
    public async Task Source_should_handle_incremental_writes()
    {
        var pipe = new Pipe();
        var source = PipeSource.From(pipe.Reader);

        var collectTask = source
            .RunWith(Sink.Seq<ReadOnlyMemory<byte>>(), _materializer);

        await pipe.Writer.WriteAsync(new byte[] { 10, 20 }, CancellationToken.None);
        await pipe.Writer.FlushAsync(CancellationToken.None);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        await pipe.Writer.WriteAsync(new byte[] { 30 }, CancellationToken.None);
        await pipe.Writer.CompleteAsync();

        var result = await collectTask;
        var combined = result.SelectMany(m => m.ToArray()).ToArray();
        Assert.Equal(new byte[] { 10, 20, 30 }, combined);
    }

    [Fact(Timeout = 5000)]
    public async Task Source_from_stream_should_emit_data()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var stream = new MemoryStream(data);

        var source = StreamSource.From(stream);

        var result = await source
            .RunWith(Sink.Seq<ReadOnlyMemory<byte>>(), _materializer);

        var combined = result.SelectMany(m => m.ToArray()).ToArray();
        Assert.Equal(data, combined);
    }
}