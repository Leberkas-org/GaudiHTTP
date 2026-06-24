using System.Net;
using GaudiHTTP.Server.Context.Features;

namespace GaudiHTTP.Tests.Server.Context;

public sealed class GaudiHttpConnectionFeatureSpec
{
    [Fact(Timeout = 5000)]
    public void ConnectionId_should_store_connection_id()
    {
        var feature = new GaudiHttpConnectionFeature { ConnectionId = "conn-42" };
        Assert.Equal("conn-42", feature.ConnectionId);
    }

    [Fact(Timeout = 5000)]
    public void RemoteIpAddress_should_store_remote_endpoint()
    {
        var feature = new GaudiHttpConnectionFeature
        {
            RemoteIpAddress = IPAddress.Parse("10.0.0.1"),
            RemotePort = 9999
        };
        Assert.Equal(IPAddress.Parse("10.0.0.1"), feature.RemoteIpAddress);
        Assert.Equal(9999, feature.RemotePort);
    }

    [Fact(Timeout = 5000)]
    public void LocalEndpoint_should_store_local_endpoint()
    {
        var feature = new GaudiHttpConnectionFeature
        {
            LocalIpAddress = IPAddress.Parse("192.168.1.1"),
            LocalPort = 8080
        };
        Assert.Equal(IPAddress.Parse("192.168.1.1"), feature.LocalIpAddress);
        Assert.Equal(8080, feature.LocalPort);
    }
}
