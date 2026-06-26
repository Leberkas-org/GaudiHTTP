using System.Buffers;
using GaudiHTTP.Pooling;

namespace GaudiHTTP.Protocol.Body;

internal sealed class DrainSlot : IResettable
{
    public int StreamId { get; private set; }
    public Stream? BodyStream { get; private set; }
    public IMemoryOwner<byte>? Buffer { get; private set; }
    public CancellationTokenSource? LinkedCts { get; private set; }
    public CancellationToken RequestCt { get; private set; }
    public long? ContentLength { get; set; }
    public int ReservedWindow { get; set; }
    public int ConsecutiveSyncReads { get; private set; }
    public bool IsReadInFlight { get; private set; }
    public bool IsOrphaned { get; private set; }

    public Func<int, object>? CachedSuccessTransform { get; private set; }
    public Func<Exception, object>? CachedFailureTransform { get; private set; }

    public void Initialize(
        int streamId,
        Stream bodyStream,
        CancellationToken requestCt,
        CancellationTokenSource? linkedCts)
    {
        StreamId = streamId;
        BodyStream = bodyStream;
        RequestCt = requestCt;
        LinkedCts = linkedCts;
        CachedSuccessTransform = n => new DrainReadComplete(StreamId, n);
        CachedFailureTransform = ex => new DrainReadFailed(StreamId, ex);
    }

    public void EnsureBuffer(int chunkSize)
    {
        Buffer ??= MemoryPool<byte>.Shared.Rent(Math.Max(chunkSize, 256));
    }

    public void BeginRead() => IsReadInFlight = true;

    public void CompleteRead()
    {
        IsReadInFlight = false;
        ConsecutiveSyncReads = 0;
    }

    public void IncrementSyncReads() => ConsecutiveSyncReads++;

    public void CompleteSyncRead()
    {
        IsReadInFlight = false;
        ConsecutiveSyncReads++;
    }

    public void ResetSyncReads() => ConsecutiveSyncReads = 0;

    public void MarkOrphaned() => IsOrphaned = true;

    public void DisposeResources()
    {
        Buffer?.Dispose();
        LinkedCts?.Dispose();
    }

    public void Reset()
    {
        StreamId = 0;
        BodyStream = null;
        Buffer = null;
        LinkedCts = null;
        RequestCt = default;
        ContentLength = null;
        IsReadInFlight = false;
        IsOrphaned = false;
        ReservedWindow = 0;
        ConsecutiveSyncReads = 0;
        CachedSuccessTransform = null;
        CachedFailureTransform = null;
    }
}
