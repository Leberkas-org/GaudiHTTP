using System.Collections.Immutable;
using Akka.Streams;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;

namespace GaudiHTTP.Streams.Stages.Server;

internal sealed class ServerConnectionShape(
    Inlet<ITransportInbound> inNetwork,
    Outlet<IFeatureCollection> outResponse,
    Inlet<IFeatureCollection> inRequest,
    Outlet<ITransportOutbound> outNetwork)
    : Shape
{
    public Inlet<ITransportInbound> InNetwork { get; } = inNetwork;
    public Outlet<IFeatureCollection> OutRequest { get; } = outResponse;
    public Inlet<IFeatureCollection> InResponse { get; } = inRequest;
    public Outlet<ITransportOutbound> OutNetwork { get; } = outNetwork;

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

