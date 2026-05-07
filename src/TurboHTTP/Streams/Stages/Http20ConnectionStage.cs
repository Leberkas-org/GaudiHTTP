using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Http2;

namespace TurboHTTP.Streams.Stages;

internal sealed class Http20ConnectionStage : GraphStage<ConnectionShape>
{
    private readonly Inlet<ITransportInbound> _inNetwork = new("Http20Connection.In.Network");
    private readonly Outlet<HttpResponseMessage> _outResponse = new("Http20Connection.Out.Response");
    private readonly Inlet<HttpRequestMessage> _inRequest = new("Http20Connection.In.Request");
    private readonly Outlet<ITransportOutbound> _outNetwork = new("Http20Connection.Out.Network");
    private readonly TurboClientOptions _options;

    public override ConnectionShape Shape => new(_inNetwork, _outResponse, _inRequest, _outNetwork);

    public Http20ConnectionStage(TurboClientOptions options)
    {
        _options = options;
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new HttpConnectionStageLogic<StateMachine>(
            this,
            ops => new StateMachine(_options, ops));
}
