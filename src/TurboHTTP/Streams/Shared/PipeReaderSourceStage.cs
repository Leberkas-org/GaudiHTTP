using System.IO.Pipelines;
using Akka.Streams;
using Akka.Streams.Stage;

namespace TurboHTTP.Streams.Shared;

/// <summary>
/// A <see cref="GraphStage{TShape}"/> backed by a <see cref="PipeReader"/> that delivers
/// byte chunks downstream on demand. Used by <see cref="HttpContentExtensions.AsSource"/>
/// when the content is a <see cref="PipeBodyContent"/>.
/// <para>
/// The stage bridges System.IO.Pipelines async patterns into Akka Streams, handling
/// backpressure transparently through the PipeReader's built-in mechanism.
/// </para>
/// </summary>
internal sealed class PipeReaderSourceStage : GraphStage<SourceShape<ReadOnlyMemory<byte>>>
{
    private readonly PipeReader _reader;
    private readonly Outlet<ReadOnlyMemory<byte>> _out = new("PipeReaderSource.Out");

    public PipeReaderSourceStage(PipeReader reader)
    {
        _reader = reader;
        Shape = new SourceShape<ReadOnlyMemory<byte>>(_out);
    }

    public override SourceShape<ReadOnlyMemory<byte>> Shape { get; }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly PipeReaderSourceStage _stage;
        private Action<ReadResult>? _onReadCallback;
        private Action? _onFailCallback;
        private bool _reading;

        public Logic(PipeReaderSourceStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._out,
                onPull: TryRead,
                onDownstreamFinish: _ =>
                {
                    _stage._reader.CancelPendingRead();
                    _stage._reader.Complete();
                    CompleteStage();
                });
        }

        public override void PreStart()
        {
            _onReadCallback = GetAsyncCallback<ReadResult>(OnReadResult);
            _onFailCallback = GetAsyncCallback(() =>
            {
                _stage._reader.Complete();
                CompleteStage();
            });
        }

        private void TryRead()
        {
            if (_reading)
            {
                return;
            }

            if (_stage._reader.TryRead(out var readResult))
            {
                ProcessReadResult(readResult);
                return;
            }

            _reading = true;
            ScheduleAsyncRead();
        }

        private void ScheduleAsyncRead()
        {
            var readCb = _onReadCallback!;
            var failCb = _onFailCallback!;
            var reader = _stage._reader;

            var vt = reader.ReadAsync();
            if (vt.IsCompleted)
            {
                _reading = false;
                try
                {
                    ProcessReadResult(vt.Result);
                }
                catch
                {
                    _stage._reader.Complete();
                    CompleteStage();
                }
                return;
            }

            vt.GetAwaiter().OnCompleted(() =>
            {
                try
                {
                    readCb(vt.Result);
                }
                catch
                {
                    failCb();
                }
            });
        }

        private void OnReadResult(ReadResult result)
        {
            _reading = false;
            ProcessReadResult(result);
        }

        private void ProcessReadResult(ReadResult result)
        {
            var buffer = result.Buffer;

            if (buffer.Length > 0)
            {
                var data = buffer.FirstSpan.ToArray();
                if (buffer.IsSingleSegment)
                {
                    _stage._reader.AdvanceTo(buffer.End);
                    Push(_stage._out, data);
                    return;
                }

                var combined = new byte[buffer.Length];
                var offset = 0;
                foreach (var segment in buffer)
                {
                    segment.Span.CopyTo(combined.AsSpan(offset));
                    offset += segment.Length;
                }

                _stage._reader.AdvanceTo(buffer.End);
                Push(_stage._out, combined);
                return;
            }

            if (result.IsCompleted || result.IsCanceled)
            {
                _stage._reader.AdvanceTo(buffer.End);
                _stage._reader.Complete();
                CompleteStage();
            }
        }

        public override void PostStop()
        {
            _stage._reader.CancelPendingRead();
        }
    }
}
