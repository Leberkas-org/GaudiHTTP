namespace TurboHttp.IO.Stages;

public record StreamAcquireItem : IControlItem
{
    public RequestEndpoint Key { get; init; }
}
