namespace TurboHTTP.Protocol.Multiplexed.Body;

internal interface IBodyEncoder : IDisposable
{
    void Start(Stream bodyStream, Action<object> onMessage);
}
