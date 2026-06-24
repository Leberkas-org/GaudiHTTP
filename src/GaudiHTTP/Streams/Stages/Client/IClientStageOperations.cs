using Akka.Actor;
using Servus.Akka.Transport;

namespace TurboHTTP.Streams.Stages.Client;

internal interface IClientStageOperations
{
    void OnResponse(HttpResponseMessage response);
    void OnOutbound(ITransportOutbound item);
    void OnScheduleTimer(string name, TimeSpan duration);
    void OnCancelTimer(string name);
    IActorRef StageActor { get; }
    bool HasPendingDemand => false;
}