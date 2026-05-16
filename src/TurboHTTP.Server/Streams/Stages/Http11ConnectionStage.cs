using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http11.Server;

namespace TurboHTTP.Server.Streams.Stages;

internal sealed class Http11ConnectionStage : GraphStage<ConnectionShape>
{
    private readonly Inlet<ITransportInbound> _inNetwork = new("Http11Connection.In.Network");
    private readonly Outlet<HttpRequestMessage> _outRequest = new("Http11Connection.Out.Request");
    private readonly Inlet<HttpResponseMessage> _inResponse = new("Http11Connection.In.Response");
    private readonly Outlet<ITransportOutbound> _outNetwork = new("Http11Connection.Out.Network");
    private readonly int _maxPipelinedRequests;

    public Http11ConnectionStage(int maxPipelinedRequests = 16)
    {
        _maxPipelinedRequests = maxPipelinedRequests;
    }

    public override ConnectionShape Shape => new(_inNetwork, _outRequest, _inResponse, _outNetwork);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new HttpConnectionServerStageLogic<Http11ServerStateMachine>(this,
            ops => new Http11ServerStateMachine(ops, maxPipelineDepth: _maxPipelinedRequests));
}