using Akka;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http.Features;

namespace TurboHTTP.Streams.Stages.Server;

internal sealed class PipelineHandles
{
    public Sink<IFeatureCollection, NotUsed> RequestSink { get; }
    public IResponseDispatcher<IFeatureCollection> ResponseDispatcher { get; }
    public FairShareDispatcher Dispatcher { get; }

    public PipelineHandles(
        Sink<IFeatureCollection, NotUsed> requestSink,
        IResponseDispatcher<IFeatureCollection> responseDispatcher,
        FairShareDispatcher dispatcher)
    {
        RequestSink = requestSink;
        ResponseDispatcher = responseDispatcher;
        Dispatcher = dispatcher;
    }
}
