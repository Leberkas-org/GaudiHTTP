using Microsoft.AspNetCore.Hosting.Server.Features;

namespace TurboHTTP.Server.Context.Features;

internal interface IConnectionTagFeature
{
    int ConnectionId { get; }
    int RequestSequence { get; }
}

internal sealed class ConnectionTagFeature : IConnectionTagFeature
{
    public int ConnectionId { get; set; }
    public int RequestSequence { get; set; }
}

internal sealed class ServerAddressesFeature : IServerAddressesFeature
{
    public ICollection<string> Addresses { get; } = new List<string>();
    public bool PreferHostingUrls { get; set; }
}