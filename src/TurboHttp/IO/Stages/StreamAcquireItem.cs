namespace TurboHttp.IO.Stages;

public record StreamAcquireItem : IControlItem
{
    public HostKey Key => HostKey.Default;
}
