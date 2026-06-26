using System.Buffers;
using GaudiHTTP.Pooling;

namespace GaudiHTTP.Protocol.Body;

internal sealed class PumpSlot : IResettable
{
    public long StreamId;
    public Stream? BodyStream;
    public IMemoryOwner<byte>? Buffer;
    public CancellationTokenSource? LinkedCts;
    public CancellationToken RequestCt;
    public long? ContentLength;
    public bool IsReadInFlight;
    public bool IsOrphaned;
    public int ConsecutiveSyncReads;

    public Func<int, object>? CachedSuccessTransform { get; private set; }
    public Func<Exception, object>? CachedFailureTransform { get; private set; }

    public void Initialize(
        long streamId,
        Stream bodyStream,
        CancellationToken requestCt,
        CancellationTokenSource? linkedCts)
    {
        StreamId = streamId;
        BodyStream = bodyStream;
        RequestCt = requestCt;
        LinkedCts = linkedCts;
        CachedSuccessTransform = n => new MultiplexedDrainReadComplete(StreamId, n);
        CachedFailureTransform = ex => new MultiplexedDrainReadFailed(StreamId, ex);
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
        ConsecutiveSyncReads = 0;
        CachedSuccessTransform = null;
        CachedFailureTransform = null;
    }
}
