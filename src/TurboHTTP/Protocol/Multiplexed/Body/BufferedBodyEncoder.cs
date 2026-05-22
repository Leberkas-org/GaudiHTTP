using System.Buffers;

namespace TurboHTTP.Protocol.Multiplexed.Body;

internal sealed class BufferedBodyEncoder : IBodyEncoder
{
    private readonly CancellationTokenSource _cts = new();

    public void Start(Stream bodyStream, Action<object> onMessage) => _ = DrainAsync(new StreamContent(bodyStream), onMessage, _cts.Token);

    private static async Task DrainAsync(HttpContent content, Action<object> onMessage, CancellationToken ct)
    {
        try
        {
            var bytes = await content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            var owner = MemoryPool<byte>.Shared.Rent(bytes.Length);
            bytes.CopyTo(owner.Memory.Span);
            onMessage(new OutboundBodyChunk(owner, bytes.Length));
            onMessage(new OutboundBodyComplete());
        }
        catch (Exception ex)
        {
            onMessage(new OutboundBodyFailed(ex));
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
