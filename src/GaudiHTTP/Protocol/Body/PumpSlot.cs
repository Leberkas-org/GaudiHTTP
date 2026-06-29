using System.Buffers;
using GaudiHTTP.Pooling;

namespace GaudiHTTP.Protocol.Body;

internal sealed class PumpSlot<TStreamId> : IResettable
{
    public TStreamId StreamId { get; private set; } = default!;
    public Stream? BodyStream { get; private set; }
    public IMemoryOwner<byte>? Buffer { get; private set; }
    public CancellationTokenSource? LinkedCts { get; private set; }
    public CancellationToken RequestCt { get; private set; }
    public long? ContentLength { get; set; }
    public int ReservedWindow { get; set; }
    public int ConsecutiveSyncReads { get; private set; }
    public bool IsReadInFlight { get; private set; }
    public bool IsOrphaned { get; private set; }

    // Created once per pooled slot instead of per Initialize: the transforms read StreamId at
    // invocation (it is a property, not captured by value), so a single instance stays correct
    // across slot reuse and avoids two closure allocations per stream registration.
    public Func<int, object> CachedSuccessTransform { get; }
    public Func<Exception, object> CachedFailureTransform { get; }

    public PumpSlot()
    {
        CachedSuccessTransform = n => new BodyReadComplete<TStreamId>(StreamId, n);
        CachedFailureTransform = ex => new BodyReadFailed<TStreamId>(StreamId, ex);
    }

    public void Initialize(
        TStreamId streamId,
        Stream bodyStream,
        CancellationToken requestCt,
        CancellationTokenSource? linkedCts)
    {
        StreamId = streamId;
        BodyStream = bodyStream;
        RequestCt = requestCt;
        LinkedCts = linkedCts;
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
        StreamId = default!;
        BodyStream = null;
        Buffer = null;
        LinkedCts = null;
        RequestCt = default;
        ContentLength = null;
        ReservedWindow = 0;
        IsReadInFlight = false;
        IsOrphaned = false;
        ConsecutiveSyncReads = 0;
    }
}
