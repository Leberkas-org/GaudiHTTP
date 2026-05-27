using System.Collections.Immutable;
using Akka.Streams;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;

namespace TurboHTTP.Streams.Stages.Server;

internal sealed class ServerConnectionShape : Shape
{
    public Inlet<ITransportInbound> InNetwork { get; }
    public Outlet<IFeatureCollection> OutRequest { get; }
    public Inlet<IFeatureCollection> InResponse { get; }
    public Outlet<ITransportOutbound> OutNetwork { get; }

    public ServerConnectionShape(
        Inlet<ITransportInbound> inNetwork,
        Outlet<IFeatureCollection> outResponse,
        Inlet<IFeatureCollection> inRequest,
        Outlet<ITransportOutbound> outNetwork)
    {
        InNetwork = inNetwork;
        OutRequest = outResponse;
        InResponse = inRequest;
        OutNetwork = outNetwork;
    }

    public override ImmutableArray<Inlet> Inlets => [InNetwork, InResponse];

    public override ImmutableArray<Outlet> Outlets => [OutRequest, OutNetwork];

    public override Shape DeepCopy()
    {
        return new ServerConnectionShape(
            (Inlet<ITransportInbound>)InNetwork.CarbonCopy(),
            (Outlet<IFeatureCollection>)OutRequest.CarbonCopy(),
            (Inlet<IFeatureCollection>)InResponse.CarbonCopy(),
            (Outlet<ITransportOutbound>)OutNetwork.CarbonCopy());
    }

    public override Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
    {
        return new ServerConnectionShape(
            (Inlet<ITransportInbound>)inlets[0],
            (Outlet<IFeatureCollection>)outlets[0],
            (Inlet<IFeatureCollection>)inlets[1],
            (Outlet<ITransportOutbound>)outlets[1]);
    }
}

