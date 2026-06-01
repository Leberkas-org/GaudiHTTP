using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Streams;
using TurboHTTP.Server;
using TurboHTTP.Server.Context.Features;

namespace TurboHTTP.Streams.Stages.Server;

internal sealed class ServerPipeline
{
    private readonly Sink<IFeatureCollection, NotUsed> _requestSink;
    private readonly IDynamicHub<int, IFeatureCollection> _responseHub;
    private readonly IActorRef _coordinator;

    private ServerPipeline(
        Sink<IFeatureCollection, NotUsed> requestSink,
        IDynamicHub<int, IFeatureCollection> responseHub,
        IActorRef coordinator)
    {
        _requestSink = requestSink;
        _responseHub = responseHub;
        _coordinator = coordinator;
    }

    public static ServerPipeline Materialize(
        IGraph<FlowShape<IFeatureCollection, IFeatureCollection>, NotUsed> bridgeFlow,
        TurboServerOptions options,
        SharedKillSwitch pipelineKillSwitch,
        IMaterializer materializer,
        IActorRefFactory actorSystem)
    {
        var hub = new DynamicHub<int, IFeatureCollection>(fc => fc.Get<IConnectionTagFeature>()!.ConnectionId);

        var (requestSink, responseHub) = MergeHub.Source<IFeatureCollection>(perProducerBufferSize: 64)
            .Via(pipelineKillSwitch.Flow<IFeatureCollection>())
            .Via(bridgeFlow)
            .ToMaterialized(hub, Keep.Both)
            .Run(materializer);

        var coordinator = actorSystem.ActorOf(FairShareCoordinator.Props(
            options.Limits.MaxConcurrentRequests,
            options.Limits.MinRequestGuarantee));

        return new ServerPipeline(requestSink, responseHub, coordinator);
    }

    public Flow<IFeatureCollection, IFeatureCollection, NotUsed> CreateConnectionFlow(
        int connectionId,
        bool unordered)
    {
        var seq = 0;

        var requestPath = Flow.Create<IFeatureCollection>()
            .Select(fc =>
            {
                fc.Set<IConnectionTagFeature>(new ConnectionTagFeature
                {
                    ConnectionId = connectionId,
                    RequestSequence = seq++
                });
                return fc;
            })
            .Via(Flow.FromGraph(new FairShareAdmissionStage(connectionId, _coordinator)));

        var responsePath = _responseHub.Source(connectionId)
            .Via(Flow.FromGraph(new ResponseReorderStage(unordered)))
            .Select(fc =>
            {
                _coordinator.Tell(new FairShareCoordinator.Release(connectionId));
                return fc;
            });

        return Flow.FromSinkAndSource(
            requestPath.To(_requestSink),
            responsePath);
    }
}