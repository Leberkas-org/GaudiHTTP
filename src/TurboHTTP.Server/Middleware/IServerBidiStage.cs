using Akka;
using Akka.Streams.Dsl;

namespace TurboHTTP.Server.Middleware;

public interface IServerBidiStage
{
    BidiFlow<HttpRequestMessage, HttpRequestMessage,
             HttpResponseMessage, HttpResponseMessage, NotUsed> Create(IServiceProvider services);
}
