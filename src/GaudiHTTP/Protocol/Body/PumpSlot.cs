using System.Buffers;
using GaudiHTTP.Pooling;

namespace GaudiHTTP.Protocol.Body;

internal sealed class PumpSlot<TStreamId> : Poolable<PumpSlot<TStreamId>>
{
    public TStreamId StreamId { get; private set; } = default!;
    public Stream? BodyStream { get; private set; }
    public IMemoryOwner<byte>? Buffer { get; private set; }
    public CancellationTokenSource? LinkedCts { get; private set; }
    public CancellationToken RequestCt { get; private set; }
    public long? ContentLength { get; set; }
    public int ReservedWindow { get; set; }
    public bool IsReadInFlight { get; private set; }
    public bool IsOrphaned { get; private set; }

    // Per-stream outbound byte budget for MultiplexedBodyPump. Denominated in body bytes (not chunk
    // count): the slot may be read while positive, is debited by the actual bytes emitted, and is
    // credited back per real transport flush (MultiplexedDataFlushed) up to the per-stream cap. A
    // depleted budget parks only THIS stream, leaving sibling streams free to drain.
    public long AvailableBytes { get; set; }

    // True while this stream is sitting in the pump's ready queue awaiting a read slot. Guards
    // against enqueuing the same stream twice (a credit arriving while it is already queued).
    public bool IsQueued { get; set; }

    // Pump incarnation this slot's current read was STARTED under (set by the pump at read start).
    // Carried into the read-completion message so the pump can drop a completion that outlived a
    // reconnect (generation bump) instead of mis-routing it onto a reused stream id.
    public int Generation { get; set; }

    // Created once per pooled slot instead of per Initialize: the transforms read StreamId and
    // Generation at invocation (they are properties, not captured by value), so a single instance
    // stays correct across slot reuse and avoids two closure allocations per stream registration.
    public Func<int, object> CachedSuccessTransform { get; }
    public Func<Exception, object> CachedFailureTransform { get; }

    public PumpSlot()
    {
        CachedSuccessTransform = n => new BodyReadComplete<TStreamId>(StreamId, n, Generation);
        CachedFailureTransform = ex => new BodyReadFailed<TStreamId>(StreamId, ex, Generation);
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
        if (Buffer is not null)
        {
            return;
        }

        Buffer = MemoryPool<byte>.Shared.Rent(Math.Max(chunkSize, 256));
    }

    public void BeginRead() => IsReadInFlight = true;

    public void CompleteRead() => IsReadInFlight = false;

    public void MarkOrphaned() => IsOrphaned = true;

    public void DisposeResources()
    {
        Buffer?.Dispose();
        LinkedCts?.Dispose();
    }

    protected override void OnReset()
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
        AvailableBytes = 0;
        IsQueued = false;
        Generation = 0;
    }
}
