using Akka.Streams;
using Akka.Streams.Stage;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http3.Server;
using TurboHTTP.Server;

namespace TurboHTTP.Streams.Stages.Server;

internal sealed class Http30ServerConnectionStage : GraphStage<ServerConnectionShape>
{
    private readonly Inlet<ITransportInbound> _inNetwork = new("Http30Connection.In.Network");
    private readonly Outlet<IFeatureCollection> _outRequest = new("Http30Connection.Out.Request");
    private readonly Inlet<IFeatureCollection> _inResponse = new("Http30Connection.In.Response");
    private readonly Outlet<ITransportOutbound> _outNetwork = new("Http30Connection.Out.Network");
    private readonly Http3ConnectionOptions _options;
    private readonly IServiceProvider? _services;

    public Http30ServerConnectionStage(TurboServerOptions options, IServiceProvider? services = null)
    {
        _options = options.ToHttp3Options();
        _services = services;
    }

    public override ServerConnectionShape Shape => new(_inNetwork, _outRequest, _inResponse, _outNetwork);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new HttpConnectionServerStageLogic<Http3ServerStateMachine>(this,
            ops => new Http3ServerStateMachine(_options, ops),
            _services);
}
