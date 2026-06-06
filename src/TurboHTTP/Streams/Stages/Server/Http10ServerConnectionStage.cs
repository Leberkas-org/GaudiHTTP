using Akka.Streams;
using Akka.Streams.Stage;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http10.Server;
using TurboHTTP.Server;

namespace TurboHTTP.Streams.Stages.Server;

internal sealed class Http10ServerConnectionStage(TurboServerOptions options, IServiceProvider? services = null)
    : GraphStage<ServerConnectionShape>
{
    private readonly Inlet<ITransportInbound> _inNetwork = new("Http10Connection.In.Network");
    private readonly Outlet<IFeatureCollection> _outRequest = new("Http10Connection.Out.Request");
    private readonly Inlet<IFeatureCollection> _inResponse = new("Http10Connection.In.Response");
    private readonly Outlet<ITransportOutbound> _outNetwork = new("Http10Connection.Out.Network");
    private readonly Http1ConnectionOptions _options = options.ToHttp1Options();

    public override ServerConnectionShape Shape => new(_inNetwork, _outRequest, _inResponse, _outNetwork);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new HttpConnectionServerStageLogic<Http10ServerStateMachine>(this,
            ops => new Http10ServerStateMachine(_options, ops),
            services,
            options.MaxOutboundCoalesceCount);
}
