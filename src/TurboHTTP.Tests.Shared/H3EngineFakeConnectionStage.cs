using System.Threading.Channels;
using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.Transport;

namespace TurboHTTP.Tests.Shared;

internal sealed class H3EngineFakeConnectionStage : GraphStage<FlowShape<ITransportOutbound, ITransportInbound>>
{
    private readonly IReadOnlyList<byte[]> _serverFrames;

    public Channel<(TransportBuffer Buffer, long? StreamType)> OutboundChannel { get; } =
        Channel.CreateUnbounded<(TransportBuffer, long?)>();

    public Inlet<ITransportOutbound> In { get; } = new("h3-engine-fake.in");
    public Outlet<ITransportInbound> Out { get; } = new("h3-engine-fake.out");

    public override FlowShape<ITransportOutbound, ITransportInbound> Shape { get; }

    public H3EngineFakeConnectionStage(params byte[][] serverFrames)
    {
        _serverFrames = serverFrames;
        Shape = new FlowShape<ITransportOutbound, ITransportInbound>(In, Out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly H3EngineFakeConnectionStage _stage;
        private readonly Queue<ITransportInbound> _pendingItems = new();
        private bool _connectedSent;
        private bool _serverFramesEnqueued;

        public Logic(H3EngineFakeConnectionStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage.In,
                onPush: () =>
                {
                    var item = Grab(stage.In);

                    long? streamType = null;
                    if (item is MultiplexedData h3Data)
                    {
                        streamType = h3Data.StreamId;
                    }

                    if (item is TransportData { Buffer: var dataChunk })
                    {
                        _stage.OutboundChannel.Writer.TryWrite((
                            TransportBufferTestExtensions.FromArray(dataChunk.Span.ToArray()), streamType));
                        dataChunk.Dispose();
                    }
                    else if (item is MultiplexedData { Buffer: var buf })
                    {
                        _stage.OutboundChannel.Writer.TryWrite((
                            TransportBufferTestExtensions.FromArray(buf.Span.ToArray()), streamType));
                        buf.Dispose();
                    }

                    if (item is CompleteWrites && !_serverFramesEnqueued)
                    {
                        _serverFramesEnqueued = true;
                        EnqueueServerFrames();
                    }

                    if (!IsClosed(stage.In))
                    {
                        Pull(stage.In);
                    }

                    TryPushNext();
                },
                onUpstreamFinish: () =>
                {
                    if (!_serverFramesEnqueued)
                    {
                        _serverFramesEnqueued = true;
                        EnqueueServerFrames();
                    }

                    _stage.OutboundChannel.Writer.TryComplete();

                    if (!IsClosed(stage.Out))
                    {
                        TryPushNext();
                        if (_pendingItems.Count == 0)
                        {
                            Complete(stage.Out);
                        }
                    }
                },
                onUpstreamFailure: FailStage);

            SetHandler(stage.Out,
                onPull: TryPushNext,
                onDownstreamFinish: _ =>
                {
                    if (!IsClosed(stage.In))
                    {
                        Cancel(stage.In);
                    }
                });
        }

        private void EnqueueServerFrames()
        {
            for (var i = 0; i < _stage._serverFrames.Count; i++)
            {
                var frameBytes = _stage._serverFrames[i];
                var buf = TransportBufferTestExtensions.FromArray(frameBytes);

                if (i == 0)
                {
                    _pendingItems.Enqueue(new ServerStreamAccepted(3, StreamDirection.Unidirectional));
                    _pendingItems.Enqueue(new MultiplexedData(buf, 3));
                }
                else
                {
                    _pendingItems.Enqueue(new MultiplexedData(buf, 0));
                }
            }

            if (_stage._serverFrames.Count > 1)
            {
                _pendingItems.Enqueue(new StreamReadCompleted(0));
            }
        }

        private void TryPushNext()
        {
            if (!IsAvailable(_stage.Out))
            {
                return;
            }

            if (!_connectedSent)
            {
                _connectedSent = true;
                Push(_stage.Out, new TransportConnected(default!));
                return;
            }

            if (_pendingItems.TryDequeue(out var next))
            {
                Push(_stage.Out, next);

                if (_pendingItems.Count == 0 && IsClosed(_stage.In))
                {
                    Complete(_stage.Out);
                }
            }
        }

        public override void PreStart() => Pull(_stage.In);
    }
}
