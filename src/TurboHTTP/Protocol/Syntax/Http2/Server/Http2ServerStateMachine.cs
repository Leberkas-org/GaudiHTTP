using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Server;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Protocol.Syntax.Http2.Server;

internal sealed class Http2ServerStateMachine : IServerStateMachine
{
    private const string DrainBodyPrefix = "drain-body:";
    private const string HeadersTimeoutPrefix = "headers-timeout:";
    private const string KeepAliveTimeout = "keep-alive-timeout";
    private const string DataRateCheck = "data-rate-check";
    private const string BodyConsumptionPrefix = "body-consumption:";

    private readonly IServerStageOperations _ops;
    private readonly Http2ServerSessionManager _sessionManager;

    private readonly TimeSpan _keepAliveTimeout;
    private int _activeStreamCount;

    public bool CanAcceptResponse => _sessionManager.ActiveStreamCount > 0;
    public bool ShouldComplete => _sessionManager.ShouldComplete;
    public int MaxQueuedRequests => _sessionManager.MaxConcurrentStreams;

    public Http2ServerStateMachine(Http2ConnectionOptions options, IServerStageOperations ops)
    {
        _ops = ops ?? throw new ArgumentNullException(nameof(ops));
        ArgumentNullException.ThrowIfNull(options);

        _sessionManager = new Http2ServerSessionManager(options, ops);

        _keepAliveTimeout = options.Limits.KeepAliveTimeout;
    }

    public void PreStart()
    {
        _sessionManager.PreStart();
        _ops.OnScheduleTimer(KeepAliveTimeout, _keepAliveTimeout);
    }

    public void DecodeClientData(ITransportInbound data)
    {
        if (data is not TransportData { Buffer: var buffer })
        {
            return;
        }

        _sessionManager.DecodeClientData(buffer);

        var streamCount = _sessionManager.ActiveStreamCount;
        switch (streamCount)
        {
            case > 0 when _activeStreamCount == 0:
                _activeStreamCount = streamCount;
                _ops.OnCancelTimer(KeepAliveTimeout);
                break;
            case 0 when _activeStreamCount > 0:
                _activeStreamCount = 0;
                _ops.OnScheduleTimer(KeepAliveTimeout, _keepAliveTimeout);
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
            _sessionManager.EmitGoAway(0, Http2ErrorCode.NoError, "Keep-alive timeout");
            return;
        }

        if (name.StartsWith(DrainBodyPrefix))
        {
            if (int.TryParse(name.AsSpan(DrainBodyPrefix.Length), out var drainStreamId))
            {
                _sessionManager.DrainOutboundBuffer(drainStreamId);
            }

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

    public void OnBodyMessage(object msg) => _sessionManager.OnBodyMessage(msg);

    public void Cleanup() => _sessionManager.Cleanup();
}