using System.Net.Security;
using Akka.Actor;
using Akka.Event;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Syntax.Http10.Server;
using GaudiHTTP.Protocol.Syntax.Http11.Server;
using GaudiHTTP.Protocol.Syntax.Http2.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Server.Context.Features;
using GaudiHTTP.Streams.Stages.Server;

namespace GaudiHTTP.Protocol;

internal sealed class ProtocolNegotiatingStateMachine : IServerStateMachine
{
    private enum Phase { WaitingForConnect, Sniffing, Running }

    private readonly GaudiServerOptions _options;
    private readonly UpgradeAwareOps _wrappedOps;
    private readonly bool _http1Allowed;
    private readonly bool _http2Allowed;
    private readonly int _maxSniffBytes;

    // Pre-protocol guards: the sniffing window has no state machine yet, so it must bound how much
    // it buffers and how long it waits before a protocol is identified (memory-exhaustion / slow-loris).
    private const string NegotiationTimer = "negotiation-headers";

    private Phase _phase = Phase.WaitingForConnect;
    private IServerStateMachine? _inner;
    private readonly List<ITransportInbound> _buffered = [];
    private long _bufferedBytes;
    private bool _sniffAborted;
    private bool _negotiationTimerActive;

    public bool CanAcceptResponse => _phase == Phase.Running && _inner!.CanAcceptResponse;
    public bool ShouldComplete => _sniffAborted || (_phase == Phase.Running && _inner!.ShouldComplete);
    public int MaxQueuedRequests => _phase == Phase.Running ? _inner!.MaxQueuedRequests : 1;

    // Forward the concurrency limit so a negotiated/sniffed HTTP/1.x connection still serializes
    // handler dispatch (RFC 9112 §9.3.2). Until a protocol is chosen no request is dispatched, so
    // the conservative default of 1 is safe.
    public int MaxConcurrentRequests => _phase == Phase.Running ? _inner!.MaxConcurrentRequests : 1;

    public ProtocolNegotiatingStateMachine(GaudiServerOptions options, IServerStageOperations ops,
        HttpProtocols allowedProtocols = HttpProtocols.Http1AndHttp2)
    {
        _options = options;
        _wrappedOps = new UpgradeAwareOps(ops, this);
        _http1Allowed = (allowedProtocols & HttpProtocols.Http1) != 0;
        _http2Allowed = (allowedProtocols & HttpProtocols.Http2) != 0;
        _maxSniffBytes = options.Limits.MaxProtocolSniffBytes;
    }

    public void PreStart()
    {
        if (_phase == Phase.Running)
        {
            _inner!.PreStart();
        }
    }

    public void DecodeClientData(ITransportInbound data)
    {
        switch (_phase)
        {
            case Phase.WaitingForConnect:
                OnWaitingForConnect(data);
                break;
            case Phase.Sniffing:
                OnSniffing(data);
                break;
            case Phase.Running:
                _inner!.DecodeClientData(data);
                break;
        }
    }

    public void OnResponse(IFeatureCollection features) => _inner!.OnResponse(features);
    public void OnDownstreamFinished() => _inner?.OnDownstreamFinished();
    public void OnBodyMessage(object msg) => _inner?.OnBodyMessage(msg);

    public void OnTimerFired(string name)
    {
        if (name == NegotiationTimer)
        {
            _negotiationTimerActive = false;
            if (_phase != Phase.Running)
            {
                // Connected but never sent an identifiable request line / preface within the
                // window — abort instead of holding the slot open indefinitely (slow-loris).
                _sniffAborted = true;
            }

            return;
        }

        _inner?.OnTimerFired(name);
    }

    public void Cleanup()
    {
        CancelNegotiationTimer();
        _inner?.Cleanup();
        DisposeBuffered();
    }

    private void ScheduleNegotiationTimer()
    {
        var timeout = _options.Limits.RequestHeadersTimeout;
        if (timeout > TimeSpan.Zero && !_negotiationTimerActive)
        {
            _negotiationTimerActive = true;
            _wrappedOps.OnScheduleTimer(NegotiationTimer, timeout);
        }
    }

    private void CancelNegotiationTimer()
    {
        if (_negotiationTimerActive)
        {
            _negotiationTimerActive = false;
            _wrappedOps.OnCancelTimer(NegotiationTimer);
        }
    }

    private void OnWaitingForConnect(ITransportInbound data)
    {
        if (data is not TransportConnected { Info.Security: var security })
        {
            return;
        }

        if (security?.ApplicationProtocol == SslApplicationProtocol.Http2)
        {
            var h2Options = _options.ToHttp2Options();
            Activate(ops => new Http2ServerStateMachine(h2Options, ops));
            _inner!.DecodeClientData(data);
            return;
        }

        if (security is not null)
        {
            var h1Options = _options.ToHttp1Options();
            var h2UpgradeOptions = _options.ToHttp2Options();
            Activate(ops => new Http11ServerStateMachine(h1Options, h2UpgradeOptions, ops, allowH2cUpgrade: _http2Allowed));
            _inner!.DecodeClientData(data);
            return;
        }

        _buffered.Add(data);
        _phase = Phase.Sniffing;
        ScheduleNegotiationTimer();
    }

    private static ReadOnlySpan<byte> Http2PrefixMagic => "PRI "u8;
    private static ReadOnlySpan<byte> Http10VersionTag => "HTTP/1.0\r\n"u8;

    private void OnSniffing(ITransportInbound data)
    {
        _buffered.Add(data);

        if (data is not TransportData { Buffer: var buffer })
        {
            return;
        }

        _bufferedBytes += buffer.Length;

        var span = buffer.Memory.Span;
        if (span.Length >= 4)
        {
            if (span.StartsWith(Http2PrefixMagic))
            {
                // Per-endpoint Protocols restriction: a cleartext endpoint that does not allow HTTP/2
                // must reject a prior-knowledge h2c preface rather than silently upgrading.
                if (!_http2Allowed)
                {
                    _sniffAborted = true;
                    CancelNegotiationTimer();
                    return;
                }

                var h2Options = _options.ToHttp2Options();
                Activate(ops => new Http2ServerStateMachine(h2Options, ops));
                ReplayBuffered();
                return;
            }

            if (DetectHttp10())
            {
                if (!_http1Allowed)
                {
                    _sniffAborted = true;
                    CancelNegotiationTimer();
                    return;
                }

                var h1Options = _options.ToHttp1Options();
                Activate(ops => new Http10ServerStateMachine(h1Options, ops));
                ReplayBuffered();
                return;
            }

            if (ContainsRequestLineCrlf())
            {
                if (!_http1Allowed)
                {
                    _sniffAborted = true;
                    CancelNegotiationTimer();
                    return;
                }

                var h1Options = _options.ToHttp1Options();
                var h2UpgradeOptions = _options.ToHttp2Options();
                Activate(ops => new Http11ServerStateMachine(h1Options, h2UpgradeOptions, ops, allowH2cUpgrade: _http2Allowed));
                ReplayBuffered();
                return;
            }
        }

        // No protocol identified from the buffered bytes yet. Bound how much we buffer while
        // waiting so an unidentifiable cleartext peer can't grow the sniff buffer without bound
        // (memory-exhaustion DoS). A real request line / HTTP/2 preface is tiny, so exceeding the
        // cap without identification means garbage/abuse — abort before any state machine exists.
        // The cap is checked AFTER identification so a large first segment carrying a valid preface
        // plus request data (common for concurrent / large HTTP/2) is recognized rather than aborted.
        if (_bufferedBytes > _maxSniffBytes)
        {
            _sniffAborted = true;
            CancelNegotiationTimer();
        }
    }

    private bool DetectHttp10()
    {
        foreach (var item in _buffered)
        {
            if (item is TransportData { Buffer: var buf } && buf.Memory.Span.IndexOf(Http10VersionTag) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private bool ContainsRequestLineCrlf()
    {
        foreach (var item in _buffered)
        {
            if (item is TransportData { Buffer: var buf } && buf.Memory.Span.IndexOf((byte)'\n') >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private void Activate(Func<IServerStageOperations, IServerStateMachine> factory)
    {
        CancelNegotiationTimer();
        _inner = factory(_wrappedOps);
        _phase = Phase.Running;
        _inner.PreStart();
    }

    private void ReplayBuffered()
    {
        var buffered = _buffered.ToArray();
        _buffered.Clear();

        foreach (var item in buffered)
        {
            _inner!.DecodeClientData(item);
        }
    }

    private void DisposeBuffered()
    {
        foreach (var item in _buffered)
        {
            if (item is TransportData { Buffer: var buf })
            {
                buf.Dispose();
            }
        }

        _buffered.Clear();
    }

    internal void HandleUpgrade(Func<IServerStageOperations, IServerStateMachine> newSmFactory)
    {
        _inner?.Cleanup();
        _inner = newSmFactory(_wrappedOps);
        _inner.PreStart();
    }

    private sealed class UpgradeAwareOps(IServerStageOperations real, ProtocolNegotiatingStateMachine parent)
        : IServerStageOperations, IProtocolSwitchCapable
    {
        public void OnRequest(IFeatureCollection features) => real.OnRequest(features);
        public void OnOutbound(ITransportOutbound item) => real.OnOutbound(item);
        public void OnScheduleTimer(string name, TimeSpan delay) => real.OnScheduleTimer(name, delay);
        public void OnCancelTimer(string name) => real.OnCancelTimer(name);
        public ILoggingAdapter Log => real.Log;
        public IActorRef StageActor => real.StageActor;
        public Akka.Streams.IMaterializer Materializer => real.Materializer;
        public IServiceProvider? Services => real.Services;
        public GaudiHttpConnectionFeature? ConnectionFeature => real.ConnectionFeature;
        public TlsHandshakeFeature? TlsHandshakeFeature => real.TlsHandshakeFeature;
        public GaudiHTTP.Pooling.ConnectionObjectPool? PoolContext => real.PoolContext;

        public void RequestProtocolSwitch(Func<IServerStageOperations, IServerStateMachine> newSmFactory)
        {
            parent.HandleUpgrade(newSmFactory);
        }
    }
}
