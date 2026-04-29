using System.Collections.Immutable;
using Akka.Streams;
using Servus.Akka.Transport;

namespace TurboHTTP.Streams.Stages;

internal sealed class ConnectionShape : Shape
{
    public Inlet<ITransportInbound> InServer { get; }
    public Outlet<HttpResponseMessage> OutResponse { get; }
    public Inlet<HttpRequestMessage> InApp { get; }
    public Outlet<ITransportOutbound> OutNetwork { get; }

    public ConnectionShape(
        Inlet<ITransportInbound> inServer,
        Outlet<HttpResponseMessage> outResponse,
        Inlet<HttpRequestMessage> inApp,
        Outlet<ITransportOutbound> outNetwork)
    {
        InServer = inServer;
        OutResponse = outResponse;
        InApp = inApp;
        OutNetwork = outNetwork;
    }

    public override ImmutableArray<Inlet> Inlets => [InServer, InApp];

    public override ImmutableArray<Outlet> Outlets => [OutResponse, OutNetwork];

    public override Shape DeepCopy()
    {
        return new ConnectionShape(
            (Inlet<ITransportInbound>)InServer.CarbonCopy(),
            (Outlet<HttpResponseMessage>)OutResponse.CarbonCopy(),
            (Inlet<HttpRequestMessage>)InApp.CarbonCopy(),
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
