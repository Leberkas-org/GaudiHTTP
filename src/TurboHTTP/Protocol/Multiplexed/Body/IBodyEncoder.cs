namespace TurboHTTP.Protocol.Multiplexed.Body;

internal interface IBodyEncoder : IDisposable
{
    void Start(HttpContent content, Action<object> onMessage);
}
