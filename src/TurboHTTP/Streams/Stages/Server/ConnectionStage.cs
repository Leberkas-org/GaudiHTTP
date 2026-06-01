using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Servus.Akka.Transport;
using TurboHTTP.Server;

namespace TurboHTTP.Streams.Stages.Server;

internal sealed class ConnectionStage(
    TurboServerOptions options,
    PipelineHandles pipelineHandles,
    IServerProtocolEngine engine,
    SharedKillSwitch? drainSwitch = null,
    IServiceProvider? services = null)
{
    public SharedKillSwitch DrainSwitch
    {
        get
        {
            field ??= KillSwitches.Shared(string.Concat("drain-", Guid.NewGuid()));
            return field;
        }
    } = drainSwitch;

    public IGraph<FlowShape<Flow<ITransportOutbound, ITransportInbound, NotUsed>, NotUsed>, NotUsed> CreateFlow(
        TaskCompletionSource<Done> completionTcs)
    {
        return new StageImpl(
            options,
            pipelineHandles,
            engine,
            DrainSwitch,
            services,
            completionTcs);
    }

    private sealed class
        StageImpl : GraphStage<FlowShape<Flow<ITransportOutbound, ITransportInbound, NotUsed>, NotUsed>>
    {
        internal readonly TurboServerOptions Options;
        internal readonly PipelineHandles PipelineHandles;
        internal readonly IServerProtocolEngine Engine;
        internal readonly SharedKillSwitch DrainSwitch;
        internal readonly IServiceProvider? Services;
        internal readonly TaskCompletionSource<Done> CompletionTcs;

        internal readonly Inlet<Flow<ITransportOutbound, ITransportInbound, NotUsed>> Inlet =
            new("ConnectionStage.In");

        internal readonly Outlet<NotUsed> Outlet = new("ConnectionStage.Out");

        public override FlowShape<Flow<ITransportOutbound, ITransportInbound, NotUsed>, NotUsed> Shape { get; }

        public StageImpl(
            TurboServerOptions options,
            PipelineHandles pipelineHandles,
            IServerProtocolEngine engine,
            SharedKillSwitch drainSwitch,
            IServiceProvider? services,
            TaskCompletionSource<Done> completionTcs)
        {
            Options = options;
            PipelineHandles = pipelineHandles;
            Engine = engine;
            DrainSwitch = drainSwitch;
            Services = services;
            CompletionTcs = completionTcs;

            Shape = new FlowShape<Flow<ITransportOutbound, ITransportInbound, NotUsed>, NotUsed>(Inlet, Outlet);
        }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new Logic(this);
        }
    }

    private sealed class Logic : GraphStageLogic
    {
        private readonly StageImpl _stage;

        private int _connectionIdCounter;
        private int _activeCount;
        private bool _upstreamFinished;
        private Action<int>? _onConnectionCompleted;

        public Logic(StageImpl stage)
            : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage.Inlet,
                onPush: OnPush,
                onUpstreamFinish: OnUpstreamFinish,
                onUpstreamFailure: OnUpstreamFailure);

            SetHandler(stage.Outlet, onPull: () => Pull(stage.Inlet));
        }

        public override void PreStart()
        {
            _onConnectionCompleted = GetAsyncCallback<int>(OnConnectionCompleted);
        }

        private void OnPush()
        {
            var connectionFlow = Grab(_stage.Inlet);
            var limit = _stage.Options.Limits.MaxConcurrentConnections;

            if (limit > 0 && _activeCount >= limit)
            {
                RejectConnection(connectionFlow);
                Pull(_stage.Inlet);
                return;
            }

            var connectionId = ++_connectionIdCounter;
            MaterializeConnection(connectionFlow, connectionId);
            Pull(_stage.Inlet);
        }

        private void OnUpstreamFinish()
        {
            _upstreamFinished = true;
            if (_activeCount == 0)
            {
                DoCompleteStage();
            }
        }

        private void OnUpstreamFailure(Exception e)
        {
            _upstreamFinished = true;
            _stage.CompletionTcs.TrySetException(e);
            FailStage(e);
        }

        private void MaterializeConnection(
            Flow<ITransportOutbound, ITransportInbound, NotUsed> connectionFlow,
            int connectionId)
        {
            try
            {
                var protocolBidi = _stage.Engine.CreateFlow(_stage.Services);
                var isH2OrH3 = _stage.Engine.ProtocolVersion.Major >= 2;
                var bridgeFlow =
                    ConnectionFlowFactory.Create(connectionId, _stage.PipelineHandles, unordered: isH2OrH3);
                var composed = protocolBidi.Join(bridgeFlow);

                var completionTask = connectionFlow
                    .Via(_stage.DrainSwitch.Flow<ITransportInbound>())
                    .ViaMaterialized(
                        Flow.Create<ITransportInbound>().WatchTermination(Keep.Right),
                        Keep.Right)
                    .Join(composed)
                    .Run(SubFusingMaterializer);

                _activeCount++;
                completionTask.ContinueWith(
                    _ => _onConnectionCompleted!(connectionId),
                    TaskContinuationOptions.ExecuteSynchronously);
            }
            catch (Exception ex)
            {
                FailStage(ex);
            }
        }

        private void OnConnectionCompleted(int connectionId)
        {
            _activeCount--;
            if (_upstreamFinished && _activeCount == 0)
            {
                DoCompleteStage();
            }
        }

        private void RejectConnection(Flow<ITransportOutbound, ITransportInbound, NotUsed> connectionFlow)
        {
            try
            {
                var killSwitch = KillSwitches.Shared(string.Concat("reject-", Guid.NewGuid()));

                Source.Empty<ITransportOutbound>()
                    .Via(connectionFlow)
                    .Via(killSwitch.Flow<ITransportInbound>())
                    .RunWith(Sink.Ignore<ITransportInbound>().MapMaterializedValue(_ => NotUsed.Instance),
                        SubFusingMaterializer);

                killSwitch.Shutdown();
            }
            catch (Exception ex)
            {
                FailStage(ex);
            }
        }

        private void DoCompleteStage()
        {
            _stage.CompletionTcs.TrySetResult(Done.Instance);
            CompleteStage();
        }
    }
}