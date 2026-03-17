namespace TurboHttp.IO.Stages;

public record MaxConcurrentStreamsItem(int MaxStreams) : IControlItem
{
    public HostKey Key => HostKey.Default;
}
