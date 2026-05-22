using System.Buffers;
using System.IO.Pipelines;
using Akka;
using Akka.Streams.Dsl;
using Akka.Util;
using Microsoft.AspNetCore.Http.Features;

namespace TurboHTTP.Context.Features;

internal sealed class TurboHttpResponseBodyFeature : ITurboResponseBodyFeature
{
    private readonly Pipe _pipe = new();
    private Func<Task>? _onStarting;
    private bool _started;
    private bool _completed;

    internal void SetOnStarting(Func<Task> onStarting) => _onStarting = onStarting;

    internal bool HasStarted => _started;

    public Stream Stream => field ??= _pipe.Writer.AsStream();

    public PipeWriter Writer => _pipe.Writer;

    public Sink<ReadOnlyMemory<byte>, Task> BodySink
    {
        get
        {
            if (field == null)
            {
                var sink = Sink.ForEachAsync<ReadOnlyMemory<byte>>(1, async chunk =>
                {
                    await EnsureStartedAsync();
                    var memory = _pipe.Writer.GetMemory(chunk.Length);
                    chunk.CopyTo(memory);
                    _pipe.Writer.Advance(chunk.Length);
                    await _pipe.Writer.FlushAsync();
                });
                field = sink.MapMaterializedValue(task =>
                    task.ContinueWith(_ => Task.CompletedTask, TaskScheduler.Default).Unwrap());
            }

            return field;
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await EnsureStartedAsync();
    }

    public async Task SendFileAsync(string path, long offset, long? count,
        CancellationToken cancellationToken = default)
    {
        await EnsureStartedAsync();
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4 * 1024,
            useAsync: true);
        if (offset > 0)
        {
            fs.Seek(offset, SeekOrigin.Begin);
        }

        var remaining = count ?? long.MaxValue;
        var buffer = ArrayPool<byte>.Shared.Rent(4 * 1024);
        try
        {
            while (remaining > 0)
            {
                var toRead = (int)Math.Min(buffer.Length, remaining);
                var read = await fs.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                var dest = _pipe.Writer.GetMemory(read);
                buffer.AsSpan(0, read).CopyTo(dest.Span);
                _pipe.Writer.Advance(read);
                await _pipe.Writer.FlushAsync(cancellationToken);
                remaining -= read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    internal void Complete()
    {
        if (!_completed)
        {
            _completed = true;
            _pipe.Writer.Complete();
        }
    }

    public async Task CompleteAsync()
    {
        if (!_completed)
        {
            _completed = true;
            await _pipe.Writer.CompleteAsync();
        }
    }

    public void DisableBuffering()
    {
    }

    internal Source<ReadOnlyMemory<byte>, NotUsed> GetResponseSource()
    {
        return Source.UnfoldAsync(_pipe.Reader, async reader =>
        {
            var readResult = await reader.ReadAsync();
            var buffer = readResult.Buffer;

            if (buffer.IsEmpty && readResult.IsCompleted)
            {
                reader.AdvanceTo(buffer.End);
                return Option<(PipeReader, ReadOnlyMemory<byte>)>.None;
            }

            if (buffer.IsEmpty)
            {
                reader.AdvanceTo(buffer.Start);
                return Option<(PipeReader, ReadOnlyMemory<byte>)>.None;
            }

            ReadOnlyMemory<byte> chunk;
            if (buffer.IsSingleSegment)
            {
                chunk = buffer.First;
            }
            else
            {
                var pooled = ArrayPool<byte>.Shared.Rent((int)buffer.Length);
                buffer.CopyTo(pooled);
                chunk = new ReadOnlyMemory<byte>(pooled, 0, (int)buffer.Length);
            }

            reader.AdvanceTo(buffer.End);

            return (reader, chunk);
        });
    }

    internal Stream GetResponseStream() => _pipe.Reader.AsStream();

    private async Task EnsureStartedAsync()
    {
        if (!_started)
        {
            _started = true;
            if (_onStarting is not null)
            {
                await _onStarting();
            }
        }
    }
}
