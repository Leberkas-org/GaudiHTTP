using Akka.Actor;
using Akka.Event;
using Servus.Akka.Transport;
using Servus.Akka.Transport.Tcp;

namespace TurboHTTP.Tests.Transport.Tcp;

public sealed class TcpTransportStateMachineSpec
{
    private sealed class StubOps : ITransportOperations
    {
        public readonly List<ITransportInbound> PushedInbound = [];
        public int PullCount;
        public bool Completed;
        public readonly Dictionary<string, TimeSpan> Timers = new();
        public readonly HashSet<string> CancelledTimers = [];

        public void OnPushInbound(ITransportInbound item) => PushedInbound.Add(item);
        public void OnSignalPullOutbound() => PullCount++;
        public void OnCompleteStage() => Completed = true;
        public void OnScheduleTimer(string key, TimeSpan delay) => Timers[key] = delay;
        public void OnCancelTimer(string key) => CancelledTimers.Add(key);
        public ILoggingAdapter Log => NoLogger.Instance;
    }

    private sealed class StubPoolingStrategy : IPoolingStrategy
    {
        public int MaxConnectionsPerHost => 6;
        public TimeSpan IdleTimeout => TimeSpan.FromSeconds(30);
        public TimeSpan ConnectionLifetime => TimeSpan.FromMinutes(5);
        public bool CanReuse(TransportOptions options) => true;
        public PoolAction OnRelease(TransportOptions options) => PoolAction.Reuse;
        public PoolAction OnIdle(object lease) => PoolAction.Reuse;
        public PoolAction OnDisconnect(object lease, DisconnectReason reason) => PoolAction.Dispose;
        public PoolAction OnUpstreamFinish(object lease) => PoolAction.Reuse;
    }

    [Fact(Timeout = 5000)]
    public void HandlePush_ConnectTransport_should_schedule_connect_timeout()
    {
        var ops = new StubOps();
        var sm = new TcpTransportStateMachine(ops, ActorRefs.Nobody, new StubPoolingStrategy(), ActorRefs.Nobody);
        var options = new TcpTransportOptions { Host = "localhost", Port = 8080 };

        sm.HandlePush(new ConnectTransport(options));

        Assert.Contains("connect-timeout", ops.Timers.Keys);
        Assert.True(ops.PullCount > 0);
    }

    [Fact(Timeout = 5000)]
    public void HandlePush_TransportData_should_enqueue_when_not_connected()
    {
        var ops = new StubOps();
        var sm = new TcpTransportStateMachine(ops, ActorRefs.Nobody, new StubPoolingStrategy(), ActorRefs.Nobody);

        var buffer = TransportBuffer.Rent(16);
        buffer.Length = 4;
        sm.HandlePush(new TransportData(buffer));

        Assert.True(ops.PullCount > 0);
        Assert.Empty(ops.PushedInbound);
    }

    [Fact(Timeout = 5000)]
    public void HandleUpstreamFinish_should_complete_when_no_connection()
    {
        var ops = new StubOps();
        var sm = new TcpTransportStateMachine(ops, ActorRefs.Nobody, new StubPoolingStrategy(), ActorRefs.Nobody);

        sm.HandleUpstreamFinish();

        Assert.True(ops.Completed);
    }
}
