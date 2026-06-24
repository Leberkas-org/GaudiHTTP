using System.Buffers;
using TurboHTTP.Pooling;

namespace TurboHTTP.Protocol.Body;

internal class BodyDrainSlot<TStreamId> : IResettable
{
    // Identity — set once via Initialize, read-only after
    public TStreamId StreamId { get; private set; } = default!;
    public Stream? BodyStream { get; private set; }
    public long? ContentLength { get; private set; }
    public CancellationToken RequestCt { get; private set; }
    public CancellationTokenSource? LinkedCts { get; private set; }

    // Cached PipeTo delegates — allocated once in BuildTransforms(), reused per read
    public Func<int, object>? CachedSuccessTransform { get; private set; }
    public Func<Exception, object>? CachedFailureTransform { get; private set; }

    // Managed resources
    public IMemoryOwner<byte>? Buffer { get; private set; }

    // Observable state
    public bool IsReadInFlight { get; private set; }
    public bool IsOrphaned { get; private set; }
    public bool IsDrainComplete { get; private set; }

    public void Initialize(
        TStreamId streamId,
        Stream bodyStream,
        long? contentLength,
        CancellationToken requestCt,
        CancellationTokenSource? linkedCts)
    {
        StreamId = streamId;
        BodyStream = bodyStream;
        ContentLength = contentLength;
        RequestCt = requestCt;
        LinkedCts = linkedCts;
        BuildTransforms();
    }

    private void BuildTransforms()
    {
        var sid = StreamId;
        CachedSuccessTransform = n => new DrainReadComplete<TStreamId>(sid, n);
        CachedFailureTransform = ex => new DrainReadFailed<TStreamId>(sid, ex);
    }

    public void EnsureBuffer(int chunkSize)
    {
        Buffer ??= MemoryPool<byte>.Shared.Rent(Math.Max(chunkSize, 256));
    }

    public void BeginRead() => IsReadInFlight = true;

    public void CompleteSyncRead() => IsReadInFlight = false;

    public void CompleteAsyncRead() => IsReadInFlight = false;

    public void MarkOrphaned() => IsOrphaned = true;

    public void MarkDrainComplete() => IsDrainComplete = true;

    public void ReplaceBuffer(int newSize)
    {
        Buffer?.Dispose();
        Buffer = MemoryPool<byte>.Shared.Rent(newSize);
    }

    public void DisposeResources()
    {
        Buffer?.Dispose();
        Buffer = null;
        LinkedCts?.Dispose();
        LinkedCts = null;
    }

    public virtual void Reset()
    {
        StreamId = default!;
        BodyStream = null;
        Buffer = null;
        LinkedCts = null;
        RequestCt = default;
        ContentLength = null;
        IsReadInFlight = false;
        IsOrphaned = false;
        IsDrainComplete = false;
        CachedSuccessTransform = null;
        CachedFailureTransform = null;
    }
}
