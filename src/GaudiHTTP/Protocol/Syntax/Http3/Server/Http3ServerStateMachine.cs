using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using GaudiHTTP.Server;
using GaudiHTTP.Streams.Stages.Server;
using static Servus.Senf;

namespace GaudiHTTP.Protocol.Syntax.Http3.Server;

internal sealed class Http3ServerStateMachine : IServerStateMachine
{
    private const string HeadersTimeoutPrefix = "headers-timeout:";
    private const string KeepAliveTimeout = "keep-alive-timeout";
    private const string DataRateCheck = "data-rate-check";
    private const string BodyConsumptionPrefix = "body-consumption:";

    private readonly IServerStageOperations _ops;
    private readonly Http3ServerSessionManager _sessionManager;

    private readonly TimeSpan _keepAliveTimeout;
    private int _activeStreamCount;

    public bool CanAcceptResponse => _sessionManager.ActiveStreamCount > 0;
    public bool ShouldComplete => _sessionManager.ShouldComplete;
    public int MaxQueuedRequests => _sessionManager.MaxConcurrentStreams;

    public Http3ServerStateMachine(Http3ConnectionOptions options, IServerStageOperations ops)
    {
        _ops = ops ?? throw new ArgumentNullException(nameof(ops));
        ArgumentNullException.ThrowIfNull(options);

        _sessionManager = new Http3ServerSessionManager(options, ops);

        _keepAliveTimeout = options.Limits.KeepAliveTimeout;
    }

    public void PreStart()
    {
        _sessionManager.PreStart();
        _ops.OnScheduleTimer(KeepAliveTimeout, _keepAliveTimeout);
    }

    public void DecodeClientData(ITransportInbound data)
    {
        _sessionManager.DecodeClientData(data);

        var streamCount = _sessionManager.ActiveStreamCount;
        if (streamCount > 0 && _activeStreamCount == 0)
        {
            _activeStreamCount = streamCount;
            _ops.OnCancelTimer(KeepAliveTimeout);
            Tracing.For("Protocol").Debug(this, "HTTP/3: first stream opened, keep-alive timer cancelled");
        }
        else if (streamCount == 0 && _activeStreamCount > 0)
        {
            _activeStreamCount = 0;
            _ops.OnScheduleTimer(KeepAliveTimeout, _keepAliveTimeout);
            Tracing.For("Protocol").Debug(this, "HTTP/3: all streams closed, keep-alive timer scheduled");
        }
        else
        {
            _activeStreamCount = streamCount;
        }
    }

    public void OnResponse(IFeatureCollection features)
    {
        _sessionManager.OnResponse(features);
    }

    public void OnDownstreamFinished()
    {
        _sessionManager.FlushAllPendingRequests();
    }

    public void OnTimerFired(string name)
    {
        if (name == KeepAliveTimeout)
        {
            Tracing.For("Protocol").Info(this, "HTTP/3: keep-alive timeout - closing connection");
            _sessionManager.SetComplete();
            return;
        }

        if (name.StartsWith(HeadersTimeoutPrefix))
        {
            if (long.TryParse(name.AsSpan(HeadersTimeoutPrefix.Length), out var streamId))
            {
                _sessionManager.EmitRstStream(streamId, ErrorCode.GeneralProtocolError);
            }

            return;
        }

        if (name == DataRateCheck)
        {
            _sessionManager.CheckDataRates();
            return;
        }

        if (name.StartsWith(BodyConsumptionPrefix) &&
            long.TryParse(name.AsSpan(BodyConsumptionPrefix.Length), out var consumptionStreamId))
        {
            _sessionManager.EmitRstStream(consumptionStreamId, ErrorCode.GeneralProtocolError);
        }
    }

    public void OnOutboundFlushed()
    {
        _sessionManager.OnOutboundFlushed();
    }

    public void OnBodyMessage(object msg)
    {
        _sessionManager.OnBodyMessage(msg);
    }

    public void Cleanup() => _sessionManager.Cleanup();
}