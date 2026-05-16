namespace TurboHTTP.Protocol.Multiplexed;

internal interface IStreamTracker<T> where T : notnull
{
    int ActiveStreamCount { get; }
    int MaxConcurrentStreams { get; }
    bool CanOpenStream();
    T AllocateStreamId();
    void SetMaxConcurrentStreams(int maxConcurrentStreams);
    void OnStreamOpened(T streamId);
    bool OnStreamClosed(T streamId);
    void Reset();
}