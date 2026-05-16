using System.Net;
using Akka;
using Akka.Streams.Dsl;
using Microsoft.Extensions.DependencyInjection;
using TurboHTTP.Server.Middleware;
using TurboHTTP.Server.Streams;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Server.Streams;

public sealed class ServerMiddlewarePipelineBuilderSpec : StreamTestBase
{
    [Fact(Timeout = 15000)]
    public async Task Build_should_return_passthrough_when_no_middleware()
    {
        var engine = Flow.Create<HttpRequestMessage>()
            .Select(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var pipeline = ServerMiddlewarePipelineBuilder.Build(engine, []);

        var response = await Source.Single(new HttpRequestMessage(HttpMethod.Get, "http://localhost/"))
            .Via(pipeline)
            .RunWith(Sink.First<HttpResponseMessage>(), Materializer);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task Build_should_stack_middleware_in_registration_order()
    {
        var callOrder = new List<string>();

        var stage1 = new TrackingBidiStage("stage1", callOrder);
        var stage2 = new TrackingBidiStage("stage2", callOrder);

        var engine = Flow.Create<HttpRequestMessage>()
            .Select(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var services = new ServiceCollection().BuildServiceProvider();
        var pipeline =
            ServerMiddlewarePipelineBuilder.Build(engine, [stage1, stage2], services);

        await Source.Single(new HttpRequestMessage(HttpMethod.Get, "http://localhost/"))
            .Via(pipeline)
            .RunWith(Sink.First<HttpResponseMessage>(), Materializer);

        Assert.Equal("stage1-req", callOrder[0]);
        Assert.Equal("stage2-req", callOrder[1]);
        Assert.Equal("stage2-res", callOrder[2]);
        Assert.Equal("stage1-res", callOrder[3]);
    }

    private sealed class TrackingBidiStage : IServerBidiStage
    {
        private readonly string _name;
        private readonly List<string> _callOrder;

        public TrackingBidiStage(string name, List<string> callOrder)
        {
            _name = name;
            _callOrder = callOrder;
        }

        public BidiFlow<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage, NotUsed>
            Create(IServiceProvider services)
        {
            var name = _name;
            var callOrder = _callOrder;
            return BidiFlow.FromFlows(
                Flow.Create<HttpRequestMessage>().Select(r =>
                {
                    callOrder.Add(name + "-req");
                    return r;
                }),
                Flow.Create<HttpResponseMessage>().Select(r =>
                {
                    callOrder.Add(name + "-res");
                    return r;
                }));
        }
    }
}