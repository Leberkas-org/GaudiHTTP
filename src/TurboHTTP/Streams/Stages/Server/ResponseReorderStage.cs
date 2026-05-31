using Akka.Streams;
using Akka.Streams.Stage;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Server.Context.Features;

namespace TurboHTTP.Streams.Stages.Server;

internal sealed class ResponseReorderStage : GraphStage<FlowShape<IFeatureCollection, IFeatureCollection>>
{
    private readonly int _connectionId;
    private readonly bool _unordered;

    private readonly Inlet<IFeatureCollection> _in = new("ResponseReorder.In");
    private readonly Outlet<IFeatureCollection> _out = new("ResponseReorder.Out");

    public override FlowShape<IFeatureCollection, IFeatureCollection> Shape { get; }

    public ResponseReorderStage(int connectionId, bool unordered)
    {
        _connectionId = connectionId;
        _unordered = unordered;
        Shape = new FlowShape<IFeatureCollection, IFeatureCollection>(_in, _out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly ResponseReorderStage _stage;
        private readonly SortedDictionary<int, IFeatureCollection> _pending = [];
        private int _nextToEmit;
        private bool _downstreamReady;
        private bool _upstreamFinished;

        public Logic(ResponseReorderStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._in,
                onPush: OnPush,
                onUpstreamFinish: () =>
                {
                    _upstreamFinished = true;
                    if (_pending.Count == 0)
                    {
                        CompleteStage();
                    }
                });

            SetHandler(stage._out,
                onPull: () =>
                {
                    _downstreamReady = true;
                    TryEmitPending();
                    if (!HasBeenPulled(stage._in) && !IsClosed(stage._in))
                    {
                        Pull(stage._in);
                    }
                });
        }

        public override void PreStart()
        {
            Pull(_stage._in);
        }

        private void OnPush()
        {
            var features = Grab(_stage._in);

            if (_stage._unordered)
            {
                if (_downstreamReady)
                {
                    _downstreamReady = false;
                    Push(_stage._out, features);
                }
                else
                {
                    var tag = features.Get<IConnectionTagFeature>();
                    var seq = tag?.RequestSequence ?? _nextToEmit++;
                    _pending[seq] = features;
                }
            }
            else
            {
                var tag = features.Get<IConnectionTagFeature>();
                var seq = tag?.RequestSequence ?? _nextToEmit;
                _pending[seq] = features;
                TryEmitPending();
            }

            if (!HasBeenPulled(_stage._in) && !IsClosed(_stage._in))
            {
                Pull(_stage._in);
            }
        }

        private void TryEmitPending()
        {
            while (_downstreamReady && _pending.ContainsKey(_nextToEmit))
            {
                _downstreamReady = false;
                Push(_stage._out, _pending[_nextToEmit]);
                _pending.Remove(_nextToEmit);
                _nextToEmit++;
            }

            if (_upstreamFinished && _pending.Count == 0)
            {
                CompleteStage();
            }
        }
    }
}
