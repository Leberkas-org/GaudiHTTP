using Akka.Streams;
using Akka.Streams.Stage;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Syntax.Http11.Server;
using GaudiHTTP.Server;

namespace GaudiHTTP.Streams.Stages.Server;

internal sealed class Http11ServerConnectionStage(TurboServerOptions options, IServiceProvider? services = null)
    : GraphStage<ServerConnectionShape>
{
    private readonly Inlet<ITransportInbound> _inNetwork = new("Http11Connection.In.Network");
    private readonly Outlet<IFeatureCollection> _outRequest = new("Http11Connection.Out.Request");
    private readonly Inlet<IFeatureCollection> _inResponse = new("Http11Connection.In.Response");
    private readonly Outlet<ITransportOutbound> _outNetwork = new("Http11Connection.Out.Network");
    private readonly Http1ConnectionOptions _options = options.ToHttp1Options();
    private readonly Http2ConnectionOptions _h2UpgradeOptions = options.ToHttp2Options();

    public override ServerConnectionShape Shape => new(_inNetwork, _outRequest, _inResponse, _outNetwork);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new HttpConnectionServerStageLogic<Http11ServerStateMachine>(this,
            ops => new Http11ServerStateMachine(_options, _h2UpgradeOptions, ops),
            services);
}
