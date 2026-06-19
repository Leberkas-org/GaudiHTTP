using System.Buffers;

namespace TurboHTTP.Protocol.Body;

internal sealed class DrainSlot
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
    }
}
