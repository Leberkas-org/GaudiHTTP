namespace TurboHTTP.Protocol.Syntax.Http3;

/// <summary>
/// Tracks HTTP/3 stream lifecycle — ID allocation, active stream count, and concurrency limits.
/// RFC 9114 §6.1: Client-initiated bidirectional stream IDs are 0, 4, 8, 12, ...
/// QUIC uses 62-bit variable-length integers, so stream IDs are <see langword="long"/>.
/// </summary>
internal sealed class StreamTracker(long initialNextStreamId = 0, int maxConcurrentStreams = 100)
{
    private readonly HashSet<long> _activeStreamIds = [];

    public int ActiveStreamCount { get; private set; }
    public int MaxConcurrentStreams { get; set; } = maxConcurrentStreams;

    /// <summary>Current next stream ID (for testing/reset visibility).</summary>
    public long NextStreamId { get; private set; } = initialNextStreamId;

    /// <summary>
    /// Returns true if a new stream can be opened without exceeding the concurrency limit.
    /// </summary>
    public bool CanOpenStream() => ActiveStreamCount < MaxConcurrentStreams;

    /// <summary>
    /// Resets to initial state for use on a new connection.
    /// Stream ID allocation restarts from 0; active set is cleared.
    /// </summary>
    public void Reset()
    {
        _activeStreamIds.Clear();
        ActiveStreamCount = 0;
        NextStreamId = 0;
    }

    /// <summary>
    /// Allocates the next client-initiated bidirectional stream ID and advances the counter by 4.
    /// RFC 9114 §6.1: Client-initiated bidirectional streams use IDs 0, 4, 8, 12, ...
    /// </summary>
    public long AllocateStreamId()
    {
        var id = NextStreamId;
        NextStreamId += 4;
        return id;
    }

    /// <summary>
    /// Registers a stream as active. Call after sending HEADERS for the stream.
    /// </summary>
    public void OnStreamOpened(long streamId)
    {
        _activeStreamIds.Add(streamId);
        ActiveStreamCount++;
    }

    /// <summary>
    /// Removes a stream from the active set. Returns false if the stream was not tracked.
    /// </summary>
    public bool OnStreamClosed(long streamId)
    {
        if (!_activeStreamIds.Remove(streamId))
        {
            return false;
        }

        ActiveStreamCount--;
        return true;
    }
}

