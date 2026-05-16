using System.Net;
using Akka.Streams.Dsl;
using TurboHTTP.Server.Streams.Stages;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Server.Middleware;

public sealed class ServerExceptionHandlerStageSpec : StreamTestBase
{
    [Fact(Timeout = 5000)]
    public async Task Stage_should_pass_through_request_and_response()
    {
        var stage = new ServerExceptionHandlerStage();
        var bidi = stage.CreateBidiFlow();

        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/test");
        var response = new HttpResponseMessage(HttpStatusCode.OK);

        var result = await Source.Single(request)
            .Via(bidi.Join(Flow.Create<HttpRequestMessage>().Select(_ => response)))
            .RunWith(Sink.First<HttpResponseMessage>(), Materializer);

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }
}