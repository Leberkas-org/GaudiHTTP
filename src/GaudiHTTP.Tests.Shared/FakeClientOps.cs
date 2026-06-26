using Akka.Actor;
using Servus.Akka.Transport;
using GaudiHTTP.Streams.Stages.Client;

namespace GaudiHTTP.Tests.Shared;

internal sealed class FakeClientOps : IClientStageOperations
{
    public List<HttpResponseMessage> Responses { get; } = [];
    public List<ITransportOutbound> Outbound { get; } = [];
    public List<object> BodyMessages { get; } = [];

    public FakeClientOps()
    {
        StageActor = new CapturingActorRef(BodyMessages);
    }

    public void OnResponse(HttpResponseMessage response) => Responses.Add(response);
    public void OnOutbound(ITransportOutbound item) => Outbound.Add(item);

    public void OnScheduleTimer(string name, TimeSpan duration)
    {
    }

    public void OnCancelTimer(string name)
    {
    }

    public IActorRef StageActor { get; }

    private sealed class CapturingActorRef(List<object> messages) : MinimalActorRef
    {
        public override ActorPath Path { get; } = new RootActorPath(new Address("akka", "test")) / "fake-ops";
        public override IActorRefProvider Provider => throw new NotSupportedException();
        protected override void TellInternal(object message, IActorRef sender) => messages.Add(message);
    }
}