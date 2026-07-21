using System.Buffers;
using System.IO.Pipelines;
using Akka.Actor;
using GaudiHTTP.Protocol;
using Servus.Akka.Transport;

namespace GaudiHTTP.Tests.Protocol;

public sealed class TcpStateMachineBaseSpec
{
    private readonly StubSm _sm = new();

    [Fact(Timeout = 5000)]
    public void DispatchLifecycleEvent_TransportConnected_should_call_OnTransportConnected_and_start_read()
    {
        var transport = new ScriptableTransport();
        transport.EnqueuePendingRead();

        var result = _sm.DispatchLifecycleEvent(new TransportConnected(ConnectionInfo.None, transport));

        Assert.True(result);
        Assert.Single(_sm.ConnectedCalls);
        Assert.Equal(1, transport.ReadCount);
    }

    [Fact(Timeout = 5000)]
    public void DispatchLifecycleEvent_TransportDisconnected_should_call_OnTransportDisconnected()
    {
        var result = _sm.DispatchLifecycleEvent(new TransportDisconnected(DisconnectReason.Graceful));

        Assert.True(result);
        Assert.Single(_sm.DisconnectedCalls);
        Assert.Equal(DisconnectReason.Graceful, _sm.DisconnectedCalls[0]);
    }

    [Fact(Timeout = 5000)]
    public void DispatchLifecycleEvent_unknown_event_should_return_false()
    {
        var result = _sm.DispatchLifecycleEvent(new TransportError(new Exception("test"), true));

        Assert.False(result);
    }

    [Fact(Timeout = 5000)]
    public void TryHandleAsyncResult_ReadCompleted_should_return_true()
    {
        var transport = new ScriptableTransport();
        transport.EnqueuePendingRead();
        _sm.DispatchLifecycleEvent(new TransportConnected(ConnectionInfo.None, transport));

        transport.EnqueuePendingRead();
        var gen = _sm.ExposedTransportGen;
        var rc = new ReadCompleted(
            new ReadResult(new ReadOnlySequence<byte>(new byte[] { 1 }), false, false), gen);

        Assert.True(_sm.TryHandleAsyncResult(rc));
        Assert.Single(_sm.DecodedData);
    }

    [Fact(Timeout = 5000)]
    public void TryHandleAsyncResult_unknown_message_should_return_false()
    {
        Assert.False(_sm.TryHandleAsyncResult("body pump message"));
    }

    [Fact(Timeout = 5000)]
    public void RequestFlush_sync_fast_path_should_call_OnFlushCompleted()
    {
        var transport = new ScriptableTransport();
        transport.EnqueuePendingRead();
        transport.FlushMode = FlushMode.Sync;

        _sm.DispatchLifecycleEvent(new TransportConnected(ConnectionInfo.None, transport));
        _sm.ExposedRequestFlush();

        Assert.Equal(1, _sm.FlushCompletedCount);
        Assert.False(_sm.ExposedIsFlushInProgress);
    }

    [Fact(Timeout = 5000)]
    public void RequestFlush_async_path_should_set_flush_in_progress()
    {
        var transport = new ScriptableTransport();
        transport.EnqueuePendingRead();
        transport.FlushMode = FlushMode.Async;

        _sm.DispatchLifecycleEvent(new TransportConnected(ConnectionInfo.None, transport));
        _sm.ExposedRequestFlush();

        Assert.True(_sm.ExposedIsFlushInProgress);
        Assert.Equal(0, _sm.FlushCompletedCount);
    }

    [Fact(Timeout = 5000)]
    public void Transport_should_be_non_null_after_connect_and_null_after_disconnect()
    {
        var transport = new ScriptableTransport();
        transport.EnqueuePendingRead();

        _sm.DispatchLifecycleEvent(new TransportConnected(ConnectionInfo.None, transport));
        Assert.NotNull(_sm.ExposedTransport);

        _sm.DispatchLifecycleEvent(new TransportDisconnected(DisconnectReason.Graceful));
        Assert.Null(_sm.ExposedTransport);
    }

    private sealed class StubOps
    {
        public IActorRef Self { get; } = new NoopActorRef();
    }

    private sealed class StubSm : TcpStateMachineBase<StubOps>
    {
        public List<ConnectionInfo> ConnectedCalls { get; } = [];
        public List<DisconnectReason> DisconnectedCalls { get; } = [];
        public List<ReadOnlySequence<byte>> DecodedData { get; } = [];
        public int FlushCompletedCount { get; private set; }

        public IConnectionTransport? ExposedTransport => Transport;
        public int ExposedTransportGen => TransportGen;
        public bool ExposedIsFlushInProgress => IsFlushInProgress;

        public void ExposedRequestFlush() => RequestFlush();

        public StubSm() : base(new StubOps()) { }

        protected override IActorRef Self => Ops.Self;
        protected override bool ShouldPauseReads => false;

        protected override (SequencePosition Consumed, SequencePosition Examined) DecodeData(
            ReadOnlySequence<byte> data)
        {
            DecodedData.Add(data);
            return (data.End, data.End);
        }

        protected override void OnTransportConnected(ConnectionInfo info) => ConnectedCalls.Add(info);
        protected override void OnTransportDisconnected(DisconnectReason reason) => DisconnectedCalls.Add(reason);
        protected override void OnFlushCompleted() => FlushCompletedCount++;
        protected override void OnFlushDeferred() { }
        protected override void OnTransportLost(Exception? ex) { }
    }

    private sealed class NoopActorRef : MinimalActorRef
    {
        public override ActorPath Path { get; } =
            new RootActorPath(new Address("akka", "test")) / "tcp-sm-base-test";

        public override IActorRefProvider Provider => throw new NotSupportedException();
        protected override void TellInternal(object message, IActorRef sender) { }
    }
}
