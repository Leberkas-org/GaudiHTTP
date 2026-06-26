using System.Buffers;
using GaudiHTTP.Pooling;

namespace GaudiHTTP.Protocol.Body;

internal sealed class DrainSlot : IResettable
{
    public int StreamId;
    public Stream? BodyStream;
    public IMemoryOwner<byte>? Buffer;
    public CancellationTokenSource? LinkedCts;
    public CancellationToken RequestCt;
    public long? ContentLength;
    public bool IsReadInFlight;
    public bool IsOrphaned;
    public ReadOnlyMemory<byte> LimboData;
    public bool HasLimbo;
    public bool IsDrainComplete;
    public int ConsecutiveSyncReads;

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

    public void StoreLimbo(ReadOnlyMemory<byte> data)
    {
        LimboData = data;
        HasLimbo = true;
    }

    public void ClearLimbo()
    {
        LimboData = default;
        HasLimbo = false;
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
        LimboData = default;
        HasLimbo = false;
        IsDrainComplete = false;
        ConsecutiveSyncReads = 0;
        CachedSuccessTransform = null;
        CachedFailureTransform = null;
    }
}
