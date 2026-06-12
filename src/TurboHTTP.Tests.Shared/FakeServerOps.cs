using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Tests.Shared;

internal sealed class FakeServerOps : IServerStageOperations
{
    public List<IFeatureCollection> Requests { get; } = [];

    public List<ITransportOutbound> Outbound { get; } = [];
    public List<(string Name, TimeSpan Delay)> ScheduledTimers { get; } = [];
    public List<string> CancelledTimers { get; } = [];

    /// <summary>Every OnScheduleTimer call in order, without the de-duplication applied to <see cref="ScheduledTimers"/>.</summary>
    public List<(string Name, TimeSpan Delay)> ScheduleTimerCalls { get; } = [];

    public void OnRequest(IFeatureCollection features) => Requests.Add(features);
    public void OnOutbound(ITransportOutbound item) => Outbound.Add(item);

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
    public IActorRef StageActor { get; set; } = ActorRefs.Nobody;
    public IMaterializer Materializer { get; set; } = null!;
}