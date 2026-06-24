using Akka.Streams;
using Akka.Streams.Stage;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Syntax.Http2.Server;
using GaudiHTTP.Server;

namespace GaudiHTTP.Streams.Stages.Server;

internal sealed class Http20ServerConnectionStage(GaudiServerOptions options, IServiceProvider? services = null)
    : GraphStage<ServerConnectionShape>
{
    private readonly Inlet<ITransportInbound> _inNetwork = new("Http20Connection.In.Network");
    private readonly Outlet<IFeatureCollection> _outRequest = new("Http20Connection.Out.Request");
    private readonly Inlet<IFeatureCollection> _inResponse = new("Http20Connection.In.Response");
    private readonly Outlet<ITransportOutbound> _outNetwork = new("Http20Connection.Out.Network");
    private readonly Http2ConnectionOptions _options = options.ToHttp2Options();

    public override ServerConnectionShape Shape => new(_inNetwork, _outRequest, _inResponse, _outNetwork);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new HttpConnectionServerStageLogic<Http2ServerStateMachine>(this,
            ops => new Http2ServerStateMachine(_options, ops),
            services);
}
