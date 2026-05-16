using System.Net;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Microsoft.Extensions.DependencyInjection;
using TurboHTTP.Server;
using TurboHTTP.Server.Routing;
using TurboHTTP.Server.Streams.Stages;

namespace TurboHTTP.Tests.Server.Streams;

public sealed class RoutingStageSpec : IDisposable
{
    private readonly ActorSystem _system;
    private readonly IMaterializer _materializer;

    public RoutingStageSpec()
    {
        _system = ActorSystem.Create("test");
        _materializer = _system.Materializer();
    }

    [Fact(Timeout = 5000)]
    public async Task Stage_should_route_request_to_matching_handler()
    {
        var routeTable = new RouteTableBuilder()
            .Add("GET", "/api/health", _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)))
            .Build();

        var stage = new RoutingStage(
            routeTable,
            new TurboConnectionInfo("test", null, 0, null, 0),
            new ServiceCollection().BuildServiceProvider(),
            CancellationToken.None);
        var flow = Flow.FromGraph(stage);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/api/health");
        var result = await Source.Single(request)
            .Via(flow)
            .RunWith(Sink.First<HttpResponseMessage>(), _materializer);

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Stage_should_return_404_for_unmatched_route_without_fallback()
    {
        var routeTable = new RouteTableBuilder().Build();

        var stage = new RoutingStage(
            routeTable,
            new TurboConnectionInfo("test", null, 0, null, 0),
            new ServiceCollection().BuildServiceProvider(),
            CancellationToken.None);
        var flow = Flow.FromGraph(stage);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/api/unknown");
        var result = await Source.Single(request)
            .Via(flow)
            .RunWith(Sink.First<HttpResponseMessage>(), _materializer);

        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Stage_should_populate_route_values_in_context()
    {
        string? capturedId = null;
        var routeTable = new RouteTableBuilder()
            .Add("GET", "/api/orders/{id}", ctx =>
            {
                capturedId = ctx.RouteValues["id"]?.ToString();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            })
            .Build();

        var stage = new RoutingStage(
            routeTable,
            new TurboConnectionInfo("test", null, 0, null, 0),
            new ServiceCollection().BuildServiceProvider(),
            CancellationToken.None);
        var flow = Flow.FromGraph(stage);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/api/orders/42");
        await Source.Single(request)
            .Via(flow)
            .RunWith(Sink.First<HttpResponseMessage>(), _materializer);

        Assert.Equal("42", capturedId);
    }

    public void Dispose()
    {
        _system.Dispose();
    }
}
