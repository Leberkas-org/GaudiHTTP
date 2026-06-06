namespace TurboHTTP.Protocol.Body;

internal readonly struct FlushResult(bool isCompleted)
{
    public bool IsCompleted { get; } = isCompleted;
}
