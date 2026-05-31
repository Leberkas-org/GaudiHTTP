using Akka.Streams;
using Akka.Streams.Stage;
using Microsoft.AspNetCore.Http.Features;

namespace TurboHTTP.Streams.Stages.Server;

internal sealed class FairShareAdmissionStage : GraphStage<FlowShape<IFeatureCollection, IFeatureCollection>>
{
    private readonly int _connectionId;
    private readonly FairShareDispatcher _dispatcher;

    private readonly Inlet<IFeatureCollection> _in = new("FairShareAdmission.In");
    private readonly Outlet<IFeatureCollection> _out = new("FairShareAdmission.Out");

    public override FlowShape<IFeatureCollection, IFeatureCollection> Shape { get; }

    public FairShareAdmissionStage(int connectionId, FairShareDispatcher dispatcher)
    {
        _connectionId = connectionId;
        _dispatcher = dispatcher;
        Shape = new FlowShape<IFeatureCollection, IFeatureCollection>(_in, _out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly FairShareAdmissionStage _stage;
        private IFeatureCollection? _stashed;
        private Action? _onSlotAvailable;

        public Logic(FairShareAdmissionStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._in,
                onPush: OnPush,
                onUpstreamFinish: () =>
                {
                    if (_stashed is null)
                    {
                        CompleteStage();
                    }
                });

            SetHandler(stage._out,
                onPull: () =>
                {
                    if (_stashed is not null)
                    {
                        TryDispatchStashed();
                    }
                    else if (!HasBeenPulled(stage._in))
                    {
                        Pull(stage._in);
                    }
                });
        }

        public override void PreStart()
        {
            _onSlotAvailable = GetAsyncCallback(OnSlotAvailable);
        }

        public override void PostStop()
        {
            _stage._dispatcher.UnregisterConnection(_stage._connectionId);
        }

        private void OnPush()
        {
            var features = Grab(_stage._in);

            if (!_stage._dispatcher.TryAcquire(_stage._connectionId))
            {
                _stashed = features;
                _stage._dispatcher.RegisterSlotAvailableCallback(
                    _stage._connectionId, _onSlotAvailable!);
                return;
            }

            Push(_stage._out, features);
        }

        private void OnSlotAvailable()
        {
            TryDispatchStashed();
        }

        private void TryDispatchStashed()
        {
            if (_stashed is not { } features)
            {
                return;
            }

            if (!_stage._dispatcher.TryAcquire(_stage._connectionId))
            {
                _stage._dispatcher.RegisterSlotAvailableCallback(
                    _stage._connectionId, _onSlotAvailable!);
                return;
            }

            _stashed = null;
            Push(_stage._out, features);

            if (!HasBeenPulled(_stage._in) && !IsClosed(_stage._in))
            {
                Pull(_stage._in);
            }
        }
    }
}
