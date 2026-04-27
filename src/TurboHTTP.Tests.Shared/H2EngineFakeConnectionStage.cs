using System.Threading.Channels;
using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.IO;

namespace TurboHTTP.Tests.Shared;

internal sealed class H2EngineFakeConnectionStage : GraphStage<FlowShape<IOutputItem, IInputItem>>
{
    private readonly IReadOnlyList<byte[]> _serverFrames;

    public Channel<NetworkBuffer> OutboundChannel { get; } =
        Channel.CreateUnbounded<NetworkBuffer>();

    public Inlet<IOutputItem> In { get; } = new("h2-engine-fake.in");
    public Outlet<IInputItem> Out { get; } = new("h2-engine-fake.out");

    public override FlowShape<IOutputItem, IInputItem> Shape { get; }

    public H2EngineFakeConnectionStage(params byte[][] serverFrames)
    {
        _serverFrames = serverFrames;
        Shape = new FlowShape<IOutputItem, IInputItem>(In, Out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private static ReadOnlySpan<byte> H2Preface => "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8;

        private readonly H2EngineFakeConnectionStage _stage;
        private bool _serverFramesSent;

        public Logic(H2EngineFakeConnectionStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage.In,
                onPush: () =>
                {
                    var item = Grab(stage.In);
                    if (item is NetworkBuffer dataChunk)
                    {
                        var span = dataChunk.Span;
                        if (span.Length >= 24 && span[..24].SequenceEqual(H2Preface))
                        {
                            var remainder = span[24..];
                            if (remainder.Length > 0)
                            {
                                stage.OutboundChannel.Writer.TryWrite(NetworkBufferTestExtensions.FromArray(remainder.ToArray()));
                            }
                        }
                        else
                        {
                            stage.OutboundChannel.Writer.TryWrite(NetworkBufferTestExtensions.FromArray(span.ToArray()));
                        }

                        dataChunk.Dispose();
                    }

                    if (!_serverFramesSent)
                    {
                        _serverFramesSent = true;
                        var frames = new IInputItem[_stage._serverFrames.Count];
                        for (var i = 0; i < _stage._serverFrames.Count; i++)
                        {
                            frames[i] = NetworkBufferTestExtensions.FromArray(_stage._serverFrames[i]);
                        }

                        EmitMultiple(stage.Out, frames, () => Pull(stage.In));
                    }
                    else
                    {
                        Pull(stage.In);
                    }
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: FailStage);

            SetHandler(stage.Out,
                onPull: () => { },
                onDownstreamFinish: _ => CompleteStage());
        }

        public override void PreStart() => Pull(_stage.In);
    }
}
