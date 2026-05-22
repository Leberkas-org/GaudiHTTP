using System.Buffers;
using Akka.Actor;

namespace TurboHTTP.Protocol.LineBased.Body;

internal sealed class ContentLengthBufferedBodyEncoder : IBodyEncoder
{
    private readonly CancellationTokenSource _cts = new();

    public void Start(HttpContent content, IActorRef stageActor)
    {
        _ = DrainAsync(content, stageActor, _cts.Token);
    }

    public void Start(Stream bodyStream, IActorRef stageActor)
    {
        Start(new StreamContent(bodyStream), stageActor);
    }

    private static async Task DrainAsync(HttpContent content, IActorRef stageActor, CancellationToken ct)
    {
        try
        {
            var bytes = await content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            var owner = MemoryPool<byte>.Shared.Rent(bytes.Length);
            bytes.CopyTo(owner.Memory.Span);
            stageActor.Tell(new OutboundBodyChunk(owner, bytes.Length));
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