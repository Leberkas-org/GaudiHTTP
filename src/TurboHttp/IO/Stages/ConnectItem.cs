namespace TurboHttp.IO.Stages;

public record ConnectItem(TcpOptions Options) : IControlItem
{
    public RequestEndpoint Key { get; init; }
}