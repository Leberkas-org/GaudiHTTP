namespace TurboHTTP.Protocol.Body;

internal interface IBodyWriter : IDisposable
{
    Memory<byte> GetMemory(int sizeHint = 0);
    void Advance(int bytes);
    ValueTask<FlushResult> FlushAsync(CancellationToken ct = default);
    ValueTask CompleteAsync(CancellationToken ct = default);
}
