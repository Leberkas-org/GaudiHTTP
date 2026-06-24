using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using GaudiHTTP.Server;
using GaudiHTTP.Streams.Stages.Server;
using static Servus.Senf;

namespace GaudiHTTP.Protocol.Syntax.Http2.Server;

internal sealed class Http2ServerStateMachine : IServerStateMachine
{
    private const string DrainBodyPrefix = "drain-body:";
    private const string HeadersTimeoutPrefix = "headers-timeout:";
    private const string KeepAliveTimeout = "keep-alive-timeout";
    private const string DataRateCheck = "data-rate-check";
    private const string BodyConsumptionPrefix = "body-consumption:";
    private const string KeepAlivePingTimer = "keep-alive-ping";
    private const string KeepAlivePingTimeoutTimer = "keep-alive-ping-timeout";

    private readonly IServerStageOperations _ops;
    private readonly Http2ServerSessionManager _sessionManager;

    private readonly TimeSpan _keepAliveTimeout;
    private readonly TimeSpan _keepAlivePingDelay;
    private readonly TimeSpan _keepAlivePingTimeout;
    private int _activeStreamCount;

    private bool KeepAlivePingEnabled => _keepAlivePingDelay != Timeout.InfiniteTimeSpan;

    public bool CanAcceptResponse => _sessionManager.ActiveStreamCount > 0;
    public bool ShouldComplete => _sessionManager.ShouldComplete;
    public int MaxQueuedRequests => _sessionManager.MaxConcurrentStreams;

    public Http2ServerStateMachine(Http2ConnectionOptions options, IServerStageOperations ops)
    {
        _ops = ops ?? throw new ArgumentNullException(nameof(ops));
        ArgumentNullException.ThrowIfNull(options);

        _sessionManager = new Http2ServerSessionManager(options, ops);

        _keepAliveTimeout = options.Limits.KeepAliveTimeout;
        _keepAlivePingDelay = options.KeepAlivePingDelay;
        _keepAlivePingTimeout = options.KeepAlivePingTimeout;
    }

    public void PreStart()
    {
        _sessionManager.PreStart();
        _ops.OnScheduleTimer(KeepAliveTimeout, _keepAliveTimeout);
        ScheduleKeepAlivePing();
    }

    public void DecodeClientData(ITransportInbound data)
    {
        if (data is not TransportData { Buffer: var buffer })
        {
            return;
        }

        _sessionManager.DecodeClientData(buffer);

        ResetKeepAlivePingTimer();

        var streamCount = _sessionManager.ActiveStreamCount;
        switch (streamCount)
        {
            case > 0 when _activeStreamCount == 0:
                _activeStreamCount = streamCount;
                _ops.OnCancelTimer(KeepAliveTimeout);
                Tracing.For("Protocol").Debug(this, "HTTP/2: first stream opened, keep-alive timer cancelled");
                break;
            case 0 when _activeStreamCount > 0:
                _activeStreamCount = 0;
                _ops.OnScheduleTimer(KeepAliveTimeout, _keepAliveTimeout);
                Tracing.For("Protocol").Debug(this, "HTTP/2: all streams closed, keep-alive timer scheduled");
                break;
            default:
                _activeStreamCount = streamCount;
                break;
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

    public void OnOutboundFlushed()
    {
        _sessionManager.OnOutboundFlushed();
    }

    public void OnBodyMessage(object msg) => _sessionManager.OnBodyMessage(msg);

    private void ScheduleKeepAlivePing()
    {
        if (KeepAlivePingEnabled)
        {
            _ops.OnScheduleTimer(KeepAlivePingTimer, _keepAlivePingDelay);
        }
    }

    private void ScheduleKeepAlivePingTimeout()
    {
        if (KeepAlivePingEnabled)
        {
            _ops.OnScheduleTimer(KeepAlivePingTimeoutTimer, _keepAlivePingTimeout);
        }
    }

    private void ResetKeepAlivePingTimer()
    {
        if (KeepAlivePingEnabled)
        {
            _ops.OnCancelTimer(KeepAlivePingTimeoutTimer);
            ScheduleKeepAlivePing();
        }
    }

    public void Cleanup() => _sessionManager.Cleanup();
}