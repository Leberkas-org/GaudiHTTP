using Akka.Actor;
using Akka.Event;
using Servus.Akka.Transport;
using TurboHTTP.Streams.Stages;

namespace TurboHTTP.Tests.Shared;

internal sealed class FakeOps : IStageOperations
{
    public List<HttpResponseMessage> Responses { get; } = [];
    public List<ITransportOutbound> Outbound { get; } = [];

    public void OnResponse(HttpResponseMessage r) => Responses.Add(r);
    public void OnOutbound(ITransportOutbound item) => Outbound.Add(item);
    public void OnScheduleTimer(string name, TimeSpan duration) { }
    public void OnCancelTimer(string name) { }
    public ILoggingAdapter Log => NoLogger.Instance;
    public IActorRef StageActor { get; set; } = ActorRefs.Nobody;
}
