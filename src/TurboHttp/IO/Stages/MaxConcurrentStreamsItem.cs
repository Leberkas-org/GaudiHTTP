namespace TurboHttp.IO.Stages;

public record MaxConcurrentStreamsItem(int MaxStreams) : IControlItem
{
    public RequestEndpoint Key { get; init; }
}