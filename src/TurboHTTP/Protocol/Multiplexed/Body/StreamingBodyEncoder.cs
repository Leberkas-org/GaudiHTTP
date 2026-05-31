using System.Buffers;

namespace TurboHTTP.Protocol.Multiplexed.Body;

internal sealed class StreamingBodyEncoder(int chunkSize = 16 * 1024) : IBodyEncoder
{
    private readonly CancellationTokenSource _cts = new();

    public void Start(Stream bodyStream, Action<object> onMessage) => _ = DrainAsync(bodyStream, onMessage, _cts.Token);

    private async Task DrainAsync(Stream stream, Action<object> onMessage, CancellationToken ct)
    {
        try
        {
            while (true)
            {
                var owner = MemoryPool<byte>.Shared.Rent(chunkSize);
                var bytesRead = await stream.ReadAsync(owner.Memory[..chunkSize], ct).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    owner.Dispose();
                    break;
                }

                onMessage(new OutboundBodyChunk(owner, bytesRead));
            }

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