namespace TurboHTTP.Protocol.Multiplexed;

internal sealed class QuicStreamTracker : IStreamTracker<long>
{
    private readonly HashSet<long> _activeStreamIds = [];

    public QuicStreamTracker(long initialNextStreamId = 0, int maxConcurrentStreams = 100)
    {
        NextStreamId = initialNextStreamId;
        MaxConcurrentStreams = maxConcurrentStreams;
    }

    public int ActiveStreamCount { get; private set; }
    public int MaxConcurrentStreams { get; private set; }
    public long NextStreamId { get; private set; }

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
