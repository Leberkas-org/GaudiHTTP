using System.Collections.Immutable;
using Akka.Streams;
using Servus.Akka.Transport;

namespace TurboHTTP.Streams.Stages;

internal sealed class ConnectionShape : Shape
{
    public Inlet<ITransportInbound> InNetwork { get; }
    public Outlet<HttpResponseMessage> OutResponse { get; }
    public Inlet<HttpRequestMessage> InRequest { get; }
    public Outlet<ITransportOutbound> OutNetwork { get; }

    public ConnectionShape(
        Inlet<ITransportInbound> inNetwork,
        Outlet<HttpResponseMessage> outResponse,
        Inlet<HttpRequestMessage> inRequest,
        Outlet<ITransportOutbound> outNetwork)
    {
        InNetwork = inNetwork;
        OutResponse = outResponse;
        InRequest = inRequest;
        OutNetwork = outNetwork;
    }

    public override ImmutableArray<Inlet> Inlets => [InNetwork, InRequest];

    public override ImmutableArray<Outlet> Outlets => [OutResponse, OutNetwork];

    public override Shape DeepCopy()
    {
        return new ConnectionShape(
            (Inlet<ITransportInbound>)InNetwork.CarbonCopy(),
            (Outlet<HttpResponseMessage>)OutResponse.CarbonCopy(),
            (Inlet<HttpRequestMessage>)InRequest.CarbonCopy(),
            (Outlet<ITransportOutbound>)OutNetwork.CarbonCopy());
    }

    public override Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
    {
        return new ConnectionShape(
            (Inlet<ITransportInbound>)inlets[0],
            (Outlet<HttpResponseMessage>)outlets[0],
            (Inlet<HttpRequestMessage>)inlets[1],
            (Outlet<ITransportOutbound>)outlets[1]);
    }
}
