using Akka;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http.Features;

namespace TurboHTTP.Streams.Stages.Server;

internal sealed class PipelineHandles(
    Sink<IFeatureCollection, NotUsed> requestSink,
    IResponseDispatcher<IFeatureCollection> responseDispatcher,
    FairShareDispatcher dispatcher)
{
    public Sink<IFeatureCollection, NotUsed> RequestSink { get; } = requestSink;
    public IResponseDispatcher<IFeatureCollection> ResponseDispatcher { get; } = responseDispatcher;
    public FairShareDispatcher Dispatcher { get; } = dispatcher;
}
