namespace TurboHTTP.Protocol.Multiplexed;

internal sealed class QuicStreamTracker(long initialNextStreamId, int maxConcurrentStreams)
    : IStreamTracker<long>
{
    private readonly HashSet<long> _activeStreamIds = [];

    public int ActiveStreamCount { get; private set; }
    public int MaxConcurrentStreams { get; private set; } = maxConcurrentStreams;
    public long NextStreamId { get; private set; } = initialNextStreamId;

    public bool CanOpenStream() => ActiveStreamCount < MaxConcurrentStreams;

    public long AllocateStreamId()
    {
        var id = NextStreamId;
        NextStreamId += 4;
        return id;
    }

    public void SetMaxConcurrentStreams(int maxConcurrentStreams)
    {
        MaxConcurrentStreams = maxConcurrentStreams;
    }

    public void OnStreamOpened(long streamId)
    {
        _activeStreamIds.Add(streamId);
        ActiveStreamCount++;
    }

    public bool OnStreamClosed(long streamId)
    {
        if (!_activeStreamIds.Remove(streamId))
        {
            return false;
        }

        ActiveStreamCount--;
        return true;
    }

    public void Reset()
    {
        _activeStreamIds.Clear();
        ActiveStreamCount = 0;
        NextStreamId = 0;
    }
}
