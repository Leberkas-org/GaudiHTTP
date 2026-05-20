using System.IO.Pipelines;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Stage;

namespace TurboHTTP.Streams.Stages;

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

    private sealed record ReadCompleted(ReadResult Result);

    private sealed record ReadFailed(Exception Error);

    private sealed class Logic : GraphStageLogic
    {
        private readonly PipeReaderSourceStage _stage;
        private IActorRef? _stageActor;
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
            _stageActor = GetStageActor(OnMessage).Ref;
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

            var vt = _stage._reader.ReadAsync();
            if (vt.IsCompleted)
            {
                _reading = false;
                ProcessReadResult(vt.Result);
                return;
            }

            _ = vt.PipeTo(_stageActor!,
                success: result => new ReadCompleted(result),
                failure: ex => new ReadFailed(ex));
        }

        private void OnMessage((IActorRef sender, object msg) args)
        {
            switch (args.msg)
            {
                case ReadCompleted completed:
                    _reading = false;
                    ProcessReadResult(completed.Result);
                    break;

                case ReadFailed ex:
                    _reading = false;
                    _stage._reader.Complete(ex.Error);
                    CompleteStage();
                    break;
            }
        }

        private void ProcessReadResult(ReadResult result)
        {
            var buffer = result.Buffer;

            if (buffer.Length > 0)
            {
                if (buffer.IsSingleSegment)
                {
                    var data = buffer.FirstSpan.ToArray();
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