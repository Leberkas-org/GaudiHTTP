using System.Buffers;

namespace GaudiHTTP.Protocol.Body;

internal sealed class PumpSlot
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
    }
}
