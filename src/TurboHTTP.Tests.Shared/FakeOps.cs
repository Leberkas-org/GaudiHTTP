using Akka.Event;
using Servus.Akka.Transport;
using TurboHTTP.Streams.Stages;

namespace TurboHTTP.Tests.Shared;

internal sealed class FakeOps : IStageOperations
{
    public List<HttpResponseMessage> Responses { get; } = [];
    public List<ITransportOutbound> Outbound { get; } = [];
    public List<(string Name, TimeSpan Duration)> Timers { get; } = [];
    public List<string> CancelledTimers { get; } = [];

    public void OnResponse(HttpResponseMessage r) => Responses.Add(r);
    public void OnOutbound(ITransportOutbound item) => Outbound.Add(item);
    public void OnScheduleTimer(string name, TimeSpan duration) => Timers.Add((name, duration));
    public void OnCancelTimer(string name) => CancelledTimers.Add(name);
    public ILoggingAdapter Log => NoLogger.Instance;
}