using GaudiHTTP.Client;
using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Syntax.Http3.Client;

namespace GaudiHTTP.Streams.Stages.Client;

internal sealed class Http30ClientConnectionStage(GaudiClientOptions options) : GraphStage<ClientConnectionShape>
{
    private readonly Inlet<ITransportInbound> _inNetwork = new("Http30Connection.In.Network");
    private readonly Outlet<HttpResponseMessage> _outResponse = new("Http30Connection.Out.Response");
    private readonly Inlet<HttpRequestMessage> _inRequest = new("Http30Connection.In.Request");
    private readonly Outlet<ITransportOutbound> _outNetwork = new("Http30Connection.Out.Network");

    public override ClientConnectionShape Shape => new(_inNetwork, _outResponse, _inRequest, _outNetwork);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new HttpClientConnectionStageLogic<Http3ClientStateMachine>(
            this,
            ops => new Http3ClientStateMachine(options, ops));
}