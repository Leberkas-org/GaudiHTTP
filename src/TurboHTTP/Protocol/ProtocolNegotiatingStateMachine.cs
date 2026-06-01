using System.Net.Security;
using Akka.Actor;
using Akka.Event;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http10.Server;
using TurboHTTP.Protocol.Syntax.Http11.Server;
using TurboHTTP.Protocol.Syntax.Http2.Server;
using TurboHTTP.Server;
using TurboHTTP.Server.Context.Features;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Protocol;

internal sealed class ProtocolNegotiatingStateMachine : IServerStateMachine
{
    private enum Phase { WaitingForConnect, Sniffing, Running }

    private readonly TurboServerOptions _options;
    private readonly UpgradeAwareOps _wrappedOps;

    private Phase _phase = Phase.WaitingForConnect;
    private IServerStateMachine? _inner;
    private readonly List<ITransportInbound> _buffered = [];

    public bool CanAcceptResponse => _phase == Phase.Running && _inner!.CanAcceptResponse;
    public bool ShouldComplete => _phase == Phase.Running && _inner!.ShouldComplete;
    public int MaxQueuedRequests => _phase == Phase.Running ? _inner!.MaxQueuedRequests : 1;

    public ProtocolNegotiatingStateMachine(TurboServerOptions options, IServerStageOperations ops)
    {
        _options = options;
        _wrappedOps = new UpgradeAwareOps(ops, this);
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
    public void OnTimerFired(string name) => _inner?.OnTimerFired(name);
    public void OnBodyMessage(object msg) => _inner?.OnBodyMessage(msg);

    public void Cleanup()
    {
        _inner?.Cleanup();
        DisposeBuffered();
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
            Activate(ops => new Http11ServerStateMachine(h1Options, h2UpgradeOptions, ops));
            _inner!.DecodeClientData(data);
            return;
        }

        _buffered.Add(data);
        _phase = Phase.Sniffing;
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

        var span = buffer.Memory.Span;
        if (span.Length < 4)
        {
            return;
        }

        if (span.StartsWith(Http2PrefixMagic))
        {
            var h2Options = _options.ToHttp2Options();
            Activate(ops => new Http2ServerStateMachine(h2Options, ops));
            ReplayBuffered();
            return;
        }

        if (DetectHttp10())
        {
            var h1Options = _options.ToHttp1Options();
            Activate(ops => new Http10ServerStateMachine(h1Options, ops));
        }
        else if (ContainsRequestLineCrlf())
        {
            var h1Options = _options.ToHttp1Options();
            var h2UpgradeOptions = _options.ToHttp2Options();
            Activate(ops => new Http11ServerStateMachine(h1Options, h2UpgradeOptions, ops));
        }
        else
        {
            return;
        }

        ReplayBuffered();
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
        public TurboHttpConnectionFeature? ConnectionFeature => real.ConnectionFeature;
        public TlsHandshakeFeature? TlsHandshakeFeature => real.TlsHandshakeFeature;

        public void RequestProtocolSwitch(Func<IServerStageOperations, IServerStateMachine> newSmFactory)
        {
            parent.HandleUpgrade(newSmFactory);
        }
    }
}
