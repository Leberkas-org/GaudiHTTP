using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using GaudiHTTP.Pooling;
using GaudiHTTP.Streams.Stages.Server;

namespace GaudiHTTP.Tests.Shared;

internal sealed class FakeServerOps : IServerStageOperations
{
    public List<IFeatureCollection> Requests { get; } = [];

    public List<ITransportOutbound> Outbound { get; } = [];
    public List<(string Name, TimeSpan Delay)> ScheduledTimers { get; } = [];
    public List<string> CancelledTimers { get; } = [];

    /// <summary>Every OnScheduleTimer call in order, without the de-duplication applied to <see cref="ScheduledTimers"/>.</summary>
    public List<(string Name, TimeSpan Delay)> ScheduleTimerCalls { get; } = [];

    public List<IFeatureCollection> ResponseBodyCompletions { get; } = [];
    public List<object> BodyMessages { get; } = [];

    public FakeServerOps()
    {
        StageActor = new CapturingActorRef(BodyMessages);
    }

    public void OnRequest(IFeatureCollection features) => Requests.Add(features);
    public void OnOutbound(ITransportOutbound item) => Outbound.Add(item);
    public void OnResponseBodyComplete(IFeatureCollection features) => ResponseBodyCompletions.Add(features);

    public void OnScheduleTimer(string name, TimeSpan delay)
    {
        ScheduleTimerCalls.Add((name, delay));
        ScheduledTimers.RemoveAll(t => t.Name == name);
        ScheduledTimers.Add((name, delay));
    }

    public void OnCancelTimer(string name)
    {
        ScheduledTimers.RemoveAll(t => t.Name == name);
        CancelledTimers.Add(name);
    }

    public ILoggingAdapter Log => NoLogger.Instance;
    public IActorRef StageActor { get; set; }
    public IMaterializer Materializer { get; set; } = null!;
    public ConnectionObjectPool? PoolContext { get; } = new();

    private sealed class CapturingActorRef(List<object> messages) : MinimalActorRef
    {
        public override ActorPath Path { get; } = new RootActorPath(new Address("akka", "test")) / "fake-server-ops";
        public override IActorRefProvider Provider => throw new NotSupportedException();
        protected override void TellInternal(object message, IActorRef sender) => messages.Add(message);
    }
}