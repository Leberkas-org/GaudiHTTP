using GaudiHTTP.Client;
using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Syntax.Http11.Client;

namespace GaudiHTTP.Streams.Stages.Client;

internal sealed class Http11ClientConnectionStage(GaudiClientOptions options) : GraphStage<ClientConnectionShape>
{
    private readonly Inlet<ITransportInbound> _inServer = new("Http11Connection.In.Network");
    private readonly Outlet<HttpResponseMessage> _outResponse = new("Http11Connection.Out.Response");
    private readonly Inlet<HttpRequestMessage> _inApp = new("Http11Connection.In.Request");
    private readonly Outlet<ITransportOutbound> _outNetwork = new("Http11Connection.Out.Network");

    public override ClientConnectionShape Shape => new(_inServer, _outResponse, _inApp, _outNetwork);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
    {
        return new HttpConnectionStageLogic<Http11ClientStateMachine>(
            this, ops => new Http11ClientStateMachine(ops, options));
    }
}