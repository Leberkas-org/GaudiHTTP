namespace GaudiHTTP.Protocol.Multiplexed;

internal readonly record struct WindowUpdateSignal<T>(T StreamId, int Increment) where T : notnull;

internal readonly struct FlowControlResult<T> where T : notnull
{
    public bool Success { get; init; }
    public bool IsConnectionViolation { get; init; }
    public bool IsStreamViolation { get; init; }
    public T ViolationStreamId { get; init; }
    public WindowUpdateSignal<T>? ConnectionWindowUpdate { get; init; }
    public WindowUpdateSignal<T>? StreamWindowUpdate { get; init; }
}