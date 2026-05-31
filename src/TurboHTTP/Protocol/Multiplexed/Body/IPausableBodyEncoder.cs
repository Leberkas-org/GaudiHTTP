namespace TurboHTTP.Protocol.Multiplexed.Body;

/// <summary>
/// A body encoder whose production loop can be paused and resumed, allowing the
/// consumer to apply backpressure when its outbound buffer fills up.
/// </summary>
internal interface IPausableBodyEncoder : IBodyEncoder
{
    void Pause();
    void Resume();
}
