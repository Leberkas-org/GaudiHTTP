using System.Buffers;
using Akka.Actor;

namespace TurboHTTP.Protocol.LineBased.Body;

internal sealed class ContentLengthStreamedBodyEncoder(int chunkSize = 16 * 1024) : IBodyEncoder
{
    private readonly CancellationTokenSource _cts = new();

    public void Start(Stream bodyStream, IActorRef stageActor)
    {
        _ = DrainAsync(bodyStream, stageActor, _cts.Token);
    }

    private async Task DrainAsync(Stream stream, IActorRef stageActor, CancellationToken ct)
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

                stageActor.Tell(new OutboundBodyChunk(owner, bytesRead));
            }

            stageActor.Tell(new OutboundBodyComplete());
        }
        catch (Exception ex)
        {
            stageActor.Tell(new OutboundBodyFailed(ex));
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}