namespace TurboHTTP.Protocol.LineBased.Body;

internal interface IBodyDecoder : IDisposable
{
    bool IsBuffered { get; }
    bool Feed(ReadOnlySpan<byte> data, out int consumed);
    bool OnEof();
    HttpContent GetContent();
}
