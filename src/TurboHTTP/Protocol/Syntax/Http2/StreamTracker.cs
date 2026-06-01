using TurboHTTP.Protocol.Multiplexed;

namespace TurboHTTP.Protocol.Syntax.Http2;

internal enum StreamAcceptResult
{
    Accepted,
    InvalidId,
    NonMonotonic,
    RefusedStream,
}

internal sealed class StreamTracker(int initialNextStreamId, int maxConcurrentStreams) : IStreamTracker<int>
{
    private int _nextStreamId = initialNextStreamId;
    private readonly HashSet<int> _activeStreamIds = [];
    private int _highestAcceptedStreamId;

    public int ActiveStreamCount { get; private set; }
    public int MaxConcurrentStreams { get; private set; } = maxConcurrentStreams;
    public int HighestAcceptedStreamId => _highestAcceptedStreamId;

    public bool CanOpenStream() => ActiveStreamCount < MaxConcurrentStreams;

    public StreamAcceptResult TryAcceptClientStream(int streamId)
    {
        if (streamId == 0 || (streamId & 1) == 0)
        {
            return StreamAcceptResult.InvalidId;
        }

        if (streamId <= _highestAcceptedStreamId)
        {
            return StreamAcceptResult.NonMonotonic;
        }

        if (!CanOpenStream())
        {
            return StreamAcceptResult.RefusedStream;
        }

        _highestAcceptedStreamId = streamId;
        OnStreamOpened(streamId);
        return StreamAcceptResult.Accepted;
    }

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
        _highestAcceptedStreamId = 0;
    }
}