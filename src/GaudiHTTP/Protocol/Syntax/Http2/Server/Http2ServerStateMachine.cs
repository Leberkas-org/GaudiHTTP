using System.Buffers;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using GaudiHTTP.Server;
using GaudiHTTP.Streams.Stages.Server;
using static Servus.Senf;

namespace GaudiHTTP.Protocol.Syntax.Http2.Server;

internal sealed class Http2ServerStateMachine :
    TcpStateMachineBase<IServerStageOperations>, IServerStateMachine
{
    private const string DrainBodyPrefix = "drain-body:";
    private const string HeadersTimeoutPrefix = "headers-timeout:";
    private const string KeepAliveTimeout = "keep-alive-timeout";
    private const string DataRateCheck = "data-rate-check";
    private const string BodyConsumptionPrefix = "body-consumption:";
    private const string KeepAlivePingTimer = "keep-alive-ping";
    private const string KeepAlivePingTimeoutTimer = "keep-alive-ping-timeout";

    private readonly Http2ServerSessionManager _sessionManager;

    private readonly TimeSpan _keepAliveTimeout;
    private readonly TimeSpan _keepAlivePingDelay;
    private readonly TimeSpan _keepAlivePingTimeout;
    private int _activeStreamCount;

    private bool KeepAlivePingEnabled => _keepAlivePingDelay != Timeout.InfiniteTimeSpan;

    public bool CanAcceptResponse => _sessionManager.ActiveStreamCount > 0;
    public bool ShouldComplete => _sessionManager.ShouldComplete;
    public int MaxQueuedRequests => _sessionManager.MaxConcurrentStreams;

    public Http2ServerStateMachine(Http2ConnectionOptions options, IServerStageOperations ops) : base(ops)
    {
        ArgumentNullException.ThrowIfNull(ops);
        ArgumentNullException.ThrowIfNull(options);

        _sessionManager = new Http2ServerSessionManager(options, ops);
        _sessionManager.EmitData = EmitToTransport;

        _keepAliveTimeout = options.Limits.KeepAliveTimeout;
        _keepAlivePingDelay = options.KeepAlivePingDelay;
        _keepAlivePingTimeout = options.KeepAlivePingTimeout;
    }

    protected override Akka.Actor.IActorRef Self => Ops.Self;
    protected override bool ShouldPauseReads => false;
    protected override void OnFlushCompleted() { }
    protected override void OnFlushDeferred() { }
    protected override void OnTransportLost(Exception? ex) => _sessionManager.ShouldComplete = true;
    protected override void OnTransportConnected(Servus.Akka.Transport.ConnectionInfo info)
    {
        _sessionManager.PreStart();
    }
    protected override void OnTransportDisconnected(DisconnectReason reason) =>
        _sessionManager.ShouldComplete = true;

    private void EmitToTransport(ReadOnlySpan<byte> data)
    {
        var mem = Transport!.GetMemory(data.Length);
        data.CopyTo(mem.Span);
        Transport.Advance(data.Length);
        RequestFlush();
    }

    public void PreStart()
    {
        Ops.OnScheduleTimer(KeepAliveTimeout, _keepAliveTimeout);
        ScheduleKeepAlivePing();
    }

    public void DecodeClientData(ITransportInbound data)
    {
        if (DispatchLifecycleEvent(data))
        {
            return;
        }

        if (data is TransportData)
        {
            throw new InvalidOperationException("TransportData is not supported on TCP state machines; use pipe transport.");
        }
    }

    public void OnResponse(IFeatureCollection features) => _sessionManager.OnResponse(features);

    public void OnDownstreamFinished()
    {
    }

    public void OnTimerFired(string name)
    {
        if (name == KeepAliveTimeout)
        {
            Tracing.For("Protocol").Info(this, "HTTP/2: keep-alive timeout - sending GOAWAY");
            _sessionManager.EmitGoAway(0, Http2ErrorCode.NoError, "Keep-alive timeout");
            _sessionManager.ShouldComplete = true;
            return;
        }

        if (name == KeepAlivePingTimer)
        {
            Tracing.For("Protocol").Trace(this, "HTTP/2: sending keep-alive PING");
            _sessionManager.SendKeepAlivePing();
            ScheduleKeepAlivePingTimeout();
            return;
        }

        if (name == KeepAlivePingTimeoutTimer)
        {
            if (_sessionManager.IsKeepAliveTimedOut(_keepAlivePingTimeout))
            {
                Tracing.For("Protocol").Info(this, "HTTP/2: keep-alive PING timeout - sending GOAWAY");
                _sessionManager.EmitGoAway(0, Http2ErrorCode.NoError, "Keep-alive PING timeout");
                _sessionManager.ShouldComplete = true;
            }
            return;
        }

        if (name.StartsWith(DrainBodyPrefix))
        {
            // No-op: body drain is now managed by the generic pump infrastructure.
            // Left as a dead-code guard for stale timers.
            return;
        }

        if (name.StartsWith(HeadersTimeoutPrefix))
        {
            if (int.TryParse(name.AsSpan(HeadersTimeoutPrefix.Length), out var streamId))
            {
                _sessionManager.EmitRstStream(streamId, Http2ErrorCode.EnhanceYourCalm);
            }

            return;
        }

        if (name == DataRateCheck)
        {
            _sessionManager.CheckDataRates();
            return;
        }

        if (name.StartsWith(BodyConsumptionPrefix) &&
            int.TryParse(name.AsSpan(BodyConsumptionPrefix.Length), out var consumptionStreamId))
        {
            _sessionManager.EmitRstStream(consumptionStreamId, Http2ErrorCode.Cancel);
        }
    }


    public void OnBodyMessage(object msg)
    {
        if (TryHandleAsyncResult(msg))
        {
            return;
        }

        _sessionManager.OnBodyMessage(msg);
    }

    protected override (SequencePosition Consumed, SequencePosition Examined) DecodeData(ReadOnlySequence<byte> data)
    {
        var consumed = _sessionManager.DecodeClientData(in data);

        ResetKeepAlivePingTimer();

        var streamCount = _sessionManager.ActiveStreamCount;
        switch (streamCount)
        {
            case > 0 when _activeStreamCount == 0:
                _activeStreamCount = streamCount;
                Ops.OnCancelTimer(KeepAliveTimeout);
                break;
            case 0 when _activeStreamCount > 0:
                _activeStreamCount = 0;
                Ops.OnScheduleTimer(KeepAliveTimeout, _keepAliveTimeout);
                break;
            default:
                _activeStreamCount = streamCount;
                break;
        }

        return (consumed, data.End);
    }

    private void ScheduleKeepAlivePing()
    {
        if (KeepAlivePingEnabled)
        {
            Ops.OnScheduleTimer(KeepAlivePingTimer, _keepAlivePingDelay);
        }
    }

    private void ScheduleKeepAlivePingTimeout()
    {
        if (KeepAlivePingEnabled)
        {
            Ops.OnScheduleTimer(KeepAlivePingTimeoutTimer, _keepAlivePingTimeout);
        }
    }

    private void ResetKeepAlivePingTimer()
    {
        if (KeepAlivePingEnabled)
        {
            Ops.OnCancelTimer(KeepAlivePingTimeoutTimer);
            ScheduleKeepAlivePing();
        }
    }

    public void Cleanup()
    {
        CleanupTransportIo();
        _sessionManager.Cleanup();
    }
}