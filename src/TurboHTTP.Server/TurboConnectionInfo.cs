using System.Net;

namespace TurboHTTP.Server;

public sealed class TurboConnectionInfo
{
    public string Id { get; }
    public IPAddress? RemoteIpAddress { get; }
    public int RemotePort { get; }
    public IPAddress? LocalIpAddress { get; }
    public int LocalPort { get; }

    public TurboConnectionInfo(
        string id,
        IPAddress? remoteIpAddress,
        int remotePort,
        IPAddress? localIpAddress,
        int localPort)
    {
        Id = id;
        RemoteIpAddress = remoteIpAddress;
        RemotePort = remotePort;
        LocalIpAddress = localIpAddress;
        LocalPort = localPort;
    }
}
