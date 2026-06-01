using Akka;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Server.Context.Features;

namespace TurboHTTP.Streams.Stages.Server;

internal static class ConnectionFlowFactory
{
    public static Flow<IFeatureCollection, IFeatureCollection, NotUsed> Create(
        int connectionId,
        PipelineHandles handles,
        bool unordered)
    {
        handles.Dispatcher.RegisterConnection(connectionId);

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
            .Via(Flow.FromGraph(new FairShareAdmissionStage(connectionId, handles.Dispatcher)));

        var responsePath = handles.ResponseDispatcher.Subscribe(connectionId)
            .Via(Flow.FromGraph(new ResponseReorderStage(connectionId, unordered)))
            .Select(fc =>
            {
                handles.Dispatcher.Release(connectionId);
                return fc;
            });

        return Flow.FromSinkAndSource(
            requestPath.To(handles.RequestSink),
            responsePath);
    }
}
