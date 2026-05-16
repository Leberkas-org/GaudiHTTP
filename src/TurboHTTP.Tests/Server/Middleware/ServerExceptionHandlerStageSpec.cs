using System.Net;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Server.Streams.Stages;

namespace TurboHTTP.Tests.Server.Middleware;

public sealed class ServerExceptionHandlerStageSpec : IDisposable
{
    private readonly ActorSystem _system;
    private readonly IMaterializer _materializer;

    public ServerExceptionHandlerStageSpec()
    {
        _system = ActorSystem.Create("test");
        _materializer = _system.Materializer();
    }

    [Fact(Timeout = 5000)]
    public async Task Stage_should_pass_through_request_and_response()
    {
        var stage = new ServerExceptionHandlerStage();
        var bidi = stage.CreateBidiFlow();

        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/test");
        var response = new HttpResponseMessage(HttpStatusCode.OK);

        var result = await Source.Single(request)
            .Via(bidi.Join(Flow.Create<HttpRequestMessage>().Select(_ => response)))
            .RunWith(Sink.First<HttpResponseMessage>(), _materializer);

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }

    // TODO: Fix syntax - Flow<T,T> Select() needs proper output type
    // [Fact(Timeout = 5000)]
    // public async Task Stage_should_return_500_when_downstream_throws()
    // {
    //     var stage = new ServerExceptionHandlerStage();
    //     var bidi = stage.CreateBidiFlow();
    //
    //     var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/test");
    //
    //     var throwingFlow = Flow.Create<HttpRequestMessage>()
    //         .SelectAsync(1, _ => Task.FromException<HttpResponseMessage>(new InvalidOperationException("boom")));
    //
    //     var result = await Source.Single(request)
    //         .Via(bidi.Join(throwingFlow))
    //         .RunWith(Sink.First<HttpResponseMessage>(), _materializer);
    //
    //     Assert.Equal(HttpStatusCode.InternalServerError, result.StatusCode);
    // }

    public void Dispose()
    {
        _system.Dispose();
    }
}
