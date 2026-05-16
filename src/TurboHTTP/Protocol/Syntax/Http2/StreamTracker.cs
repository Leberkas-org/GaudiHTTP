using TurboHTTP.Protocol.Multiplexed;

namespace TurboHTTP.Protocol.Syntax.Http2;

internal sealed class StreamTracker : IStreamTracker<int>
{
    private int _nextStreamId;
    private readonly HashSet<int> _activeStreamIds = [];

    public StreamTracker(int initialNextStreamId = 1, int maxConcurrentStreams = 100)
    {
        _nextStreamId = initialNextStreamId;
        MaxConcurrentStreams = maxConcurrentStreams;
    }

    public int ActiveStreamCount { get; private set; }
    public int MaxConcurrentStreams { get; private set; }

    public bool CanOpenStream() => ActiveStreamCount < MaxConcurrentStreams;

    public int AllocateStreamId()
    {
        var id = _nextStreamId;
        _nextStreamId += 2;
        return id;
    }

    public void SetMaxConcurrentStreams(int maxConcurrentStreams)
    {
        MaxConcurrentStreams = maxConcurrentStreams;
    }

    public void OnStreamOpened(int streamId)
    {
        _activeStreamIds.Add(streamId);
        ActiveStreamCount++;
    }

    public bool OnStreamClosed(int streamId)
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
        _nextStreamId = 1;
    }
}