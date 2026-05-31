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
