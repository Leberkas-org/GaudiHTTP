using GaudiHTTP.Client;
using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Syntax.Http2.Client;

namespace GaudiHTTP.Streams.Stages.Client;

internal sealed class Http20ClientConnectionStage(GaudiClientOptions options) : GraphStage<ClientConnectionShape>
{
    private readonly Inlet<ITransportInbound> _inNetwork = new("Http20Connection.In.Network");
    private readonly Outlet<HttpResponseMessage> _outResponse = new("Http20Connection.Out.Response");
    private readonly Inlet<HttpRequestMessage> _inRequest = new("Http20Connection.In.Request");
    private readonly Outlet<ITransportOutbound> _outNetwork = new("Http20Connection.Out.Network");

    public override ClientConnectionShape Shape => new(_inNetwork, _outResponse, _inRequest, _outNetwork);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new HttpConnectionStageLogic<Http2ClientStateMachine>(
            this,
            ops => new Http2ClientStateMachine(options, ops));
}
