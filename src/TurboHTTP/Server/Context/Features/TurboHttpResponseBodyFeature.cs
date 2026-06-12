using System.Buffers;
using System.IO.Pipelines;
using Akka;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Streams.IO;
using static Servus.Senf;

namespace TurboHTTP.Server.Context.Features;

internal sealed class TurboHttpResponseBodyFeature : IHttpResponseBodyFeature
{
    private Pipe? _pipe;
    private ArrayBufferWriter<byte> _bufferWriter = FeatureCollectionFactory.RentBuffer();
    private ResponsePipeWriter _writer;
    private Stream? _stream;
    private Sink<ReadOnlyMemory<byte>, Task>? _bodySink;

    public TurboHttpResponseBodyFeature()
    {
        _writer = new ResponsePipeWriter(this);
    }

    internal void SetResponseFeature(TurboHttpResponseFeature feature) => _writer.SetResponseFeature(feature);

    internal bool HasStarted => _writer.HasStarted;

    internal Task WhenHeadersReady => _writer.WhenHeadersReady;

    public Stream Stream => _stream ??= _writer.AsStream(leaveOpen: true);

    public PipeWriter Writer => _writer;

    internal void Reset()
    {
        _stream = null;
        _bodySink = null;

        if (_pipe is not null)
        {
            _pipe.Reader.Complete();
            _pipe.Writer.Complete();
            _pipe = null;
        }

        _bufferWriter.ResetWrittenCount();
        _writer.Reset();
    }

    internal bool HasPipe => _pipe is not null;

    internal bool TryGetBufferedBody(out ReadOnlyMemory<byte> body)
    {
        if (_pipe is null && _writer.IsCompleted && _bufferWriter.WrittenCount > 0)
        {
            body = _bufferWriter.WrittenMemory;
            return true;
        }

        if (_pipe is not null && _writer.IsCompleted && _pipe.Reader.TryRead(out var result))
        {
            if (result.IsCompleted && !result.Buffer.IsEmpty)
            {
                if (result.Buffer.IsSingleSegment)
                {
                    // Hand out the pipe's own segment: the caller copies it into the wire
                    // buffer synchronously, and the segment stays valid until the reader
                    // completes on feature reset. Examined-to-end keeps the data readable.
                    body = result.Buffer.First;
                    _pipe.Reader.AdvanceTo(result.Buffer.Start, result.Buffer.End);
                    return true;
                }

                body = result.Buffer.ToArray();
                _pipe.Reader.AdvanceTo(result.Buffer.End);
                return true;
            }

            _pipe.Reader.AdvanceTo(result.Buffer.Start);
        }

        body = default;
        return false;
    }

    internal void UpgradeToPipe()
    {
        if (_pipe is not null)
        {
            return;
        }

        Tracing.For("Stage").Debug(this, "response upgraded to pipe (buffered={0}, completed={1})",
            _bufferWriter.WrittenCount, _writer.IsCompleted);

        // The initial flush below is not awaited, so the pause threshold must exceed the
        // already-buffered content or the pending FlushAsync would be silently discarded.
        var buffered = _bufferWriter.WrittenCount;
        _pipe = buffered < 64 * 1024
            ? new Pipe()
            : new Pipe(new PipeOptions(
                pauseWriterThreshold: buffered + 64 * 1024,
                resumeWriterThreshold: buffered / 2));

        if (buffered > 0)
        {
            var src = _bufferWriter.WrittenSpan;
            var dest = _pipe.Writer.GetMemory(src.Length);
            src.CopyTo(dest.Span);
            _pipe.Writer.Advance(src.Length);
            _pipe.Writer.FlushAsync();
            _bufferWriter.ResetWrittenCount();
        }

        if (_writer.IsCompleted)
        {
            _pipe.Writer.Complete();
        }
    }

    public Sink<ReadOnlyMemory<byte>, Task> BodySink
    {
        get
        {
            if (_bodySink == null)
            {
                UpgradeToPipe();
                var pipeSink = PipeSink.To(_pipe!.Writer);
                _bodySink = Flow.Create<ReadOnlyMemory<byte>>()
                    .SelectAsync(1, chunk =>
                    {
                        _writer.CommitHeaders();
                        return Task.FromResult(chunk);
                    })
                    .ToMaterialized(pipeSink, Keep.Right);
            }

            return _bodySink;
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _writer.CommitHeadersAsync();
    }

    public async Task SendFileAsync(string path, long offset, long? count,
        CancellationToken cancellationToken = default)
    {
        UpgradeToPipe();

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

                var dest = _writer.GetMemory(read);
                buffer.AsSpan(0, read).CopyTo(dest.Span);
                _writer.Advance(read);
                await _writer.FlushAsync(cancellationToken);
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
        _writer.Complete();
    }

    public async Task CompleteAsync()
    {
        await _writer.CompleteAsync();
    }

    public void DisableBuffering()
    {
        UpgradeToPipe();
    }

    internal Source<ReadOnlyMemory<byte>, NotUsed> GetResponseSource()
    {
        UpgradeToPipe();
        return PipeSource.From(_pipe!.Reader);
    }

    internal PipeReader GetResponsePipeReader()
    {
        UpgradeToPipe();
        return _pipe!.Reader;
    }

    internal Stream GetResponseStream()
    {
        UpgradeToPipe();
        return _pipe!.Reader.AsStream();
    }

    private sealed class ResponsePipeWriter : PipeWriter
    {
        private readonly TurboHttpResponseBodyFeature _owner;
        private TaskCompletionSource? _headerCommit;
        private bool _headersCommitted;
        private TurboHttpResponseFeature? _responseFeature;

        public ResponsePipeWriter(TurboHttpResponseBodyFeature owner)
        {
            _owner = owner;
        }

        // Awaited from the stage-actor thread while the app thread commits — a true
        // cross-thread boundary, hence the explicit barriers. The TCS is lazy: handlers
        // that complete synchronously never touch it.
        public Task WhenHeadersReady
        {
            get
            {
                if (Volatile.Read(ref _headersCommitted))
                {
                    return Task.CompletedTask;
                }

                var tcs = _headerCommit;
                if (tcs is null)
                {
                    var fresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    tcs = Interlocked.CompareExchange(ref _headerCommit, fresh, null) ?? fresh;
                }

                if (Volatile.Read(ref _headersCommitted))
                {
                    tcs.TrySetResult();
                }

                return tcs.Task;
            }
        }

        public bool HasStarted { get; private set; }
        public bool IsCompleted { get; private set; }
        public long BytesWritten { get; private set; }

        public void SetResponseFeature(TurboHttpResponseFeature feature) => _responseFeature = feature;

        internal void Reset()
        {
            _responseFeature = null;
            HasStarted = false;
            IsCompleted = false;
            BytesWritten = 0;
            _headerCommit = null;
            _headersCommitted = false;
        }

        private void SignalHeadersReady()
        {
            Volatile.Write(ref _headersCommitted, true);
            _headerCommit?.TrySetResult();
        }

        public void CommitHeaders()
        {
            if (!HasStarted)
            {
                HasStarted = true;
                _owner.UpgradeToPipe();
                SignalHeadersReady();
            }
        }

        public async Task CommitHeadersAsync()
        {
            if (!HasStarted)
            {
                HasStarted = true;
                try
                {
                    if (_responseFeature is not null)
                    {
                        await _responseFeature.FireOnStartingAsync();
                    }
                }
                finally
                {
                    _owner.UpgradeToPipe();
                    SignalHeadersReady();
                }
            }
        }

        private PipeWriter? PipeWriterOrNull => _owner._pipe?.Writer;

        public override bool CanGetUnflushedBytes => true;
        public override long UnflushedBytes => PipeWriterOrNull?.UnflushedBytes ?? _owner._bufferWriter.WrittenCount;

        public override Memory<byte> GetMemory(int sizeHint = 0)
        {
            if (_owner._pipe is not null)
            {
                return _owner._pipe.Writer.GetMemory(sizeHint);
            }

            return _owner._bufferWriter.GetMemory(sizeHint);
        }

        public override Span<byte> GetSpan(int sizeHint = 0)
        {
            if (_owner._pipe is not null)
            {
                return _owner._pipe.Writer.GetSpan(sizeHint);
            }

            return _owner._bufferWriter.GetSpan(sizeHint);
        }

        public override void Advance(int bytes)
        {
            BytesWritten += bytes;

            if (_owner._pipe is not null)
            {
                _owner._pipe.Writer.Advance(bytes);
                return;
            }

            _owner._bufferWriter.Advance(bytes);
        }

        public override void CancelPendingFlush()
        {
            _owner._pipe?.Writer.CancelPendingFlush();
        }

        public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
        {
            if (!HasStarted)
            {
                return CommitAndFlushAsync(cancellationToken);
            }

            if (_owner._pipe is not null)
            {
                return _owner._pipe.Writer.FlushAsync(cancellationToken);
            }

            return new ValueTask<FlushResult>(new FlushResult(false, false));
        }

        public override ValueTask<FlushResult> WriteAsync(ReadOnlyMemory<byte> source,
            CancellationToken cancellationToken = default)
        {
            if (!HasStarted)
            {
                return CommitAndWriteAsync(source, cancellationToken);
            }

            if (_owner._pipe is not null)
            {
                return _owner._pipe.Writer.WriteAsync(source, cancellationToken);
            }

            var dest = _owner._bufferWriter.GetSpan(source.Length);
            source.Span.CopyTo(dest);
            _owner._bufferWriter.Advance(source.Length);
            BytesWritten += source.Length;
            return new ValueTask<FlushResult>(new FlushResult(false, false));
        }

        private async ValueTask<FlushResult> CommitAndFlushAsync(CancellationToken cancellationToken)
        {
            HasStarted = true;
            try
            {
                if (_responseFeature is not null)
                {
                    await _responseFeature.FireOnStartingAsync();
                }
            }
            finally
            {
                _owner.UpgradeToPipe();
                SignalHeadersReady();
            }

            return await _owner._pipe!.Writer.FlushAsync(cancellationToken);
        }

        private async ValueTask<FlushResult> CommitAndWriteAsync(ReadOnlyMemory<byte> source,
            CancellationToken cancellationToken)
        {
            HasStarted = true;
            try
            {
                if (_responseFeature is not null)
                {
                    await _responseFeature.FireOnStartingAsync();
                }
            }
            finally
            {
                _owner.UpgradeToPipe();
                SignalHeadersReady();
            }

            BytesWritten += source.Length;
            return await _owner._pipe!.Writer.WriteAsync(source, cancellationToken);
        }

        public override void Complete(Exception? exception = null)
        {
            if (!IsCompleted)
            {
                IsCompleted = true;
                CommitHeaders();
                _owner._pipe?.Writer.Complete(exception);
            }
        }

        public override ValueTask CompleteAsync(Exception? exception = null)
        {
            if (!IsCompleted)
            {
                IsCompleted = true;
                CommitHeaders();
                if (_owner._pipe is not null)
                {
                    return _owner._pipe.Writer.CompleteAsync(exception);
                }
            }

            return default;
        }
    }
}
