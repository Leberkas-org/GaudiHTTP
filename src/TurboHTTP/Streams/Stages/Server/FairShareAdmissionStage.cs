using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Stage;
using Microsoft.AspNetCore.Http.Features;

namespace TurboHTTP.Streams.Stages.Server;

internal sealed class FairShareAdmissionStage : GraphStage<FlowShape<IFeatureCollection, IFeatureCollection>>
{
    private readonly int _connectionId;
    private readonly IActorRef _coordinator;

    private readonly Inlet<IFeatureCollection> _in = new("FairShareAdmission.In");
    private readonly Outlet<IFeatureCollection> _out = new("FairShareAdmission.Out");

    public override FlowShape<IFeatureCollection, IFeatureCollection> Shape { get; }

    public FairShareAdmissionStage(int connectionId, IActorRef coordinator)
    {
        _connectionId = connectionId;
        _coordinator = coordinator;
        Shape = new FlowShape<IFeatureCollection, IFeatureCollection>(_in, _out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly FairShareAdmissionStage _stage;
        private IActorRef? _self;
        private IFeatureCollection? _stashed;
        private bool _upstreamFinished;

        public Logic(FairShareAdmissionStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._in,
                onPush: OnPush,
                onUpstreamFinish: () =>
                {
                    _upstreamFinished = true;
                    if (_stashed is null)
                    {
                        CompleteStage();
                    }
                });

            SetHandler(stage._out,
                onPull: () =>
                {
                    if (!HasBeenPulled(stage._in) && !IsClosed(stage._in))
                    {
                        Pull(stage._in);
                    }
                });
        }

        public override void PreStart()
        {
            _self = GetStageActor(OnMessage).Ref;
            _stage._coordinator.Tell(new FairShareCoordinator.Register(_stage._connectionId));
        }

        public override void PostStop()
        {
            _stage._coordinator.Tell(new FairShareCoordinator.Unregister(_stage._connectionId));
        }

        private void OnPush()
        {
            var features = Grab(_stage._in);
            _stashed = features;
            _stage._coordinator.Tell(new FairShareCoordinator.Acquire(_stage._connectionId, _self!));
        }

        private void OnMessage((IActorRef sender, object msg) args)
        {
            if (args.msg is FairShareCoordinator.Granted && _stashed is { } features)
            {
                _stashed = null;
                Push(_stage._out, features);

                if (_upstreamFinished)
                {
                    CompleteStage();
                }
                else if (!HasBeenPulled(_stage._in) && !IsClosed(_stage._in))
                {
                    Pull(_stage._in);
                }
            }
        }
    }
}
