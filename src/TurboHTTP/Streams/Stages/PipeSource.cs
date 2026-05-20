using System.IO.Pipelines;
using Akka;
using Akka.Streams.Dsl;

namespace TurboHTTP.Streams.Stages;

internal sealed class PipeSource : IAsyncDisposable
{
    private readonly Pipe _pipe;
    private bool _writerCompleted;

    public PipeSource() : this(PipeOptions.Default)
    {
    }

    public PipeSource(PipeOptions options)
    {
        _pipe = new Pipe(options);
    }

    public Source<ReadOnlyMemory<byte>, NotUsed> Source
    {
        get
        {
            field ??= Akka.Streams.Dsl.Source.FromGraph(
                new PipeReaderSourceStage(_pipe.Reader));
            return field;
        }
    }

    public PipeWriter Writer => _pipe.Writer;

    public Stream AsStream() => _pipe.Writer.AsStream(leaveOpen: true);

    public async ValueTask CompleteAsync(Exception? exception = null)
    {
        if (!_writerCompleted)
        {
            _writerCompleted = true;
            await _pipe.Writer.CompleteAsync(exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_writerCompleted)
        {
            _writerCompleted = true;
            await _pipe.Writer.CompleteAsync();
        }

        await _pipe.Reader.CompleteAsync();
    }
}