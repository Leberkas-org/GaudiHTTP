namespace GaudiHTTP.Protocol.Body;

internal interface IStreamingBodyReader : IBodyReader
{
    bool TryEnqueue(ReadOnlySpan<byte> data);
    void Complete();
    void Fault(Exception ex);
    ValueTask<BodyReadResult> ReadAsync(CancellationToken ct = default);
    void AdvanceTo();
    bool IsFull { get; }
    event Action? SlotFreed;
}
