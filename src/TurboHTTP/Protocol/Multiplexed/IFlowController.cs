using TurboHTTP.Protocol.Syntax.Http2;

namespace TurboHTTP.Protocol.Multiplexed;

internal interface IFlowController<T> where T : notnull
{
    bool GoAwayReceived { get; }
    long GetSendWindow(T streamId);
    void OnDataSent(T streamId, int length);
    void OnSendWindowUpdate(T streamId, int increment);
    FlowControlResult<T> OnInboundData(T streamId, int dataLength);
    void InitStreamSendWindow(T streamId);
    void RemoveStreamSendWindow(T streamId);
    void ApplyInitialWindowSizeDelta(long delta);
    WindowUpdateSignal<T>? OnStreamClosed(T streamId);
    void OnGoAway();
    void Reset(int connectionWindowSize, int streamWindowSize);
    SettingsResult OnRemoteSettings(SettingsFrame frame);
    PingFrame? OnPing(PingFrame ping);
}
