namespace TurboHTTP.Protocol.Body;

internal interface IBodyReader : IDisposable
{
    bool IsBuffered { get; }
    bool IsCompleted { get; }
    Stream AsStream();
}
