using System.Buffers;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Internal;
using TurboHttp.Protocol.RFC9114;

namespace TurboHttp.Streams.Stages.Encoding;

public sealed class Http30EncoderStage : GraphStage<FlowShape<Http3Frame, IOutputItem>>
{
    private readonly Inlet<Http3Frame> _in = new("Http30Encoder.In");
    private readonly Outlet<IOutputItem> _out = new("Http30Encoder.Out");

    public override FlowShape<Http3Frame, IOutputItem> Shape => new(_in, _out);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        public Logic(Http30EncoderStage stage) : base(stage.Shape)
        {
            SetHandler(stage._in, () =>
            {
                var frame = Grab(stage._in);

                var owner = MemoryPool<byte>.Shared.Rent(frame.SerializedSize);
                var span = owner.Memory.Span;

                frame.WriteTo(ref span);

                Push(stage._out, new DataItem(owner, frame.SerializedSize));
            });

            SetHandler(stage._out, () => Pull(stage._in));
        }
    }
}
