namespace TurboHttp.IO.Stages;

public record ConnectItem(TcpOptions Options) : IControlItem
{
    public HostKey Key { get; init; }
}