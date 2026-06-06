namespace TurboHTTP.Protocol.Body;

internal readonly struct BodyReadResult(ReadOnlyMemory<byte> memory, bool isCompleted)
{
    public ReadOnlyMemory<byte> Memory { get; } = memory;
    public bool IsCompleted { get; } = isCompleted;
}
