using Akka.Actor;

namespace TurboHTTP.Protocol.LineBased.Body;

internal interface IBodyEncoder : IDisposable
{
    void Start(HttpContent content, IActorRef stageActor);
    void Start(Stream bodyStream, IActorRef stageActor);
}
