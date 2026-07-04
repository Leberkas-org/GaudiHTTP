using GaudiHTTP.Client;
using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Syntax.Http10.Client;

namespace GaudiHTTP.Streams.Stages.Client;

internal sealed class Http10ClientConnectionStage(GaudiClientOptions options) : GraphStage<ClientConnectionShape>
{
    private readonly Inlet<ITransportInbound> _inNetwork = new("Http10Connection.In.Network");
    private readonly Outlet<HttpResponseMessage> _outResponse = new("Http10Connection.Out.Response");
    private readonly Inlet<HttpRequestMessage> _inRequest = new("Http10Connection.In.Request");
    private readonly Outlet<ITransportOutbound> _outNetwork = new("Http10Connection.Out.Network");

    public override ClientConnectionShape Shape => new(_inNetwork, _outResponse, _inRequest, _outNetwork);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
    {
        return new HttpClientConnectionStageLogic<Http10ClientStateMachine>(
            this, ops => new Http10ClientStateMachine(ops, options));
    }
}