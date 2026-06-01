using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Server;
using TurboHTTP.Server.Context.Features;
using TurboHTTP.Streams.Stages;

namespace TurboHTTP.Streams.Stages.Server;

internal sealed class ServerPipeline
{
    private readonly Sink<IFeatureCollection, NotUsed> _requestSink;
    private readonly IDynamicHub<int, IFeatureCollection> _responseHub;
    private readonly FairShareDispatcher _dispatcher;

    private ServerPipeline(
        Sink<IFeatureCollection, NotUsed> requestSink,
        IDynamicHub<int, IFeatureCollection> responseHub,
        FairShareDispatcher dispatcher)
    {
        _requestSink = requestSink;
        _responseHub = responseHub;
        _dispatcher = dispatcher;
    }

    public FairShareDispatcher Dispatcher => _dispatcher;

    public static ServerPipeline Materialize(
        IGraph<FlowShape<IFeatureCollection, IFeatureCollection>, NotUsed> bridgeFlow,
        TurboServerOptions options,
        SharedKillSwitch pipelineKillSwitch,
        IMaterializer materializer)
    {
        var hub = new DynamicHub<int, IFeatureCollection>(
            fc => fc.Get<IConnectionTagFeature>()!.ConnectionId);

        var (requestSink, responseHub) = MergeHub.Source<IFeatureCollection>(perProducerBufferSize: 64)
            .Via(pipelineKillSwitch.Flow<IFeatureCollection>())
            .Via(bridgeFlow)
            .ToMaterialized(hub, Keep.Both)
            .Run(materializer);

        var dispatcher = new FairShareDispatcher(
            options.Limits.MaxConcurrentRequests,
            options.Limits.MinRequestGuarantee);

        return new ServerPipeline(requestSink, responseHub, dispatcher);
    }

    public Flow<IFeatureCollection, IFeatureCollection, NotUsed> CreateConnectionFlow(
        int connectionId,
        bool unordered)
    {
        _dispatcher.RegisterConnection(connectionId);

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
            .Via(Flow.FromGraph(new FairShareAdmissionStage(connectionId, _dispatcher)));

        var responsePath = _responseHub.Source(connectionId)
            .Via(Flow.FromGraph(new ResponseReorderStage(unordered)))
            .Select(fc =>
            {
                _dispatcher.Release(connectionId);
                return fc;
            });

        return Flow.FromSinkAndSource(
            requestPath.To(_requestSink),
            responsePath);
    }
}
