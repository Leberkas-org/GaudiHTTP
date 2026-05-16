using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Server.Middleware;
using TurboHTTP.Server.Streams.Stages;

namespace TurboHTTP.Server.Streams;

internal static class ServerMiddlewarePipelineBuilder
{
    internal static Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> Build(
        Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> innerFlow,
        IReadOnlyList<IGraph<BidiShape<HttpRequestMessage, HttpRequestMessage,
            HttpResponseMessage, HttpResponseMessage>, NotUsed>> layers)
    {
        if (layers.Count == 0)
        {
            return innerFlow;
        }

        var compositeFlow = Flow.FromGraph(
            GraphDsl.Create(innerFlow, (b, engine) =>
            {
                var stages = new BidiShape<HttpRequestMessage, HttpRequestMessage,
                    HttpResponseMessage, HttpResponseMessage>[layers.Count];
                for (var i = 0; i < layers.Count; i++)
                {
                    stages[i] = b.Add(layers[i]);
                }

                for (var i = 0; i < stages.Length - 1; i++)
                {
                    b.From(stages[i + 1].Outlet1).To(stages[i].Inlet1);
                    b.From(stages[i].Outlet2).To(stages[i + 1].Inlet2);
                }

                b.From(stages[0].Outlet1).To(engine.Inlet);
                b.From(engine.Outlet).To(stages[0].Inlet2);

                return new FlowShape<HttpRequestMessage, HttpResponseMessage>(
                    stages[^1].Inlet1,
                    stages[^1].Outlet2);
            }));

        return compositeFlow;
    }

    internal static Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> Build(
        Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> innerFlow,
        IReadOnlyList<IServerBidiStage> stages,
        IServiceProvider services)
    {
        var graphs = new List<IGraph<BidiShape<HttpRequestMessage, HttpRequestMessage,
            HttpResponseMessage, HttpResponseMessage>, NotUsed>>(stages.Count + 1);

        // ExceptionHandler is always innermost (index 0)
        graphs.Add(new ServerExceptionHandlerStage());

        // User middleware outermost-first → reverse for innermost-first stacking
        for (var i = stages.Count - 1; i >= 0; i--)
        {
            graphs.Add(stages[i].Create(services));
        }

        return Build(innerFlow, graphs);
    }
}
