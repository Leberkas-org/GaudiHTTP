using Akka.Actor;
using Servus.Akka.Transport;
using TurboHTTP.Streams.Stages.Client;

namespace TurboHTTP.Tests.Shared;

internal sealed class FakeClientOps : IClientStageOperations
{
    public List<HttpResponseMessage> Responses { get; } = [];
    public List<ITransportOutbound> Outbound { get; } = [];

    public void OnResponse(HttpResponseMessage response) => Responses.Add(response);
    public void OnOutbound(ITransportOutbound item) => Outbound.Add(item);

    public void OnScheduleTimer(string name, TimeSpan duration)
    {
    }

    public void OnCancelTimer(string name)
    {
    }

    public IActorRef StageActor { get; init; } = ActorRefs.Nobody;
}