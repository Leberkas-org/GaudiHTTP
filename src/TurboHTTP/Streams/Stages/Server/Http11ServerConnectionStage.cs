using Akka.Streams;
using Akka.Streams.Stage;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http11.Server;
using TurboHTTP.Server;

namespace TurboHTTP.Streams.Stages.Server;

internal sealed class Http11ServerConnectionStage : GraphStage<ServerConnectionShape>
{
    private readonly Inlet<ITransportInbound> _inNetwork = new("Http11Connection.In.Network");
    private readonly Outlet<IFeatureCollection> _outRequest = new("Http11Connection.Out.Request");
    private readonly Inlet<IFeatureCollection> _inResponse = new("Http11Connection.In.Response");
    private readonly Outlet<ITransportOutbound> _outNetwork = new("Http11Connection.Out.Network");
    private readonly Http1ConnectionOptions _options;
    private readonly Http2ConnectionOptions _h2UpgradeOptions;
    private readonly IServiceProvider? _services;

    public Http11ServerConnectionStage(TurboServerOptions options, IServiceProvider? services = null)
    {
        _options = options.ToHttp1Options();
        _h2UpgradeOptions = options.ToHttp2Options();
        _services = services;
    }

    public override ServerConnectionShape Shape => new(_inNetwork, _outRequest, _inResponse, _outNetwork);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new HttpConnectionServerStageLogic<Http11ServerStateMachine>(this,
            ops => new Http11ServerStateMachine(_options, _h2UpgradeOptions, ops),
            _services);
}
