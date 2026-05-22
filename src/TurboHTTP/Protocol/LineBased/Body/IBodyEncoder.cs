using Akka.Actor;

namespace TurboHTTP.Protocol.LineBased.Body;

internal interface IBodyEncoder : IDisposable
{
    void Start(Stream bodyStream, IActorRef stageActor);
}
