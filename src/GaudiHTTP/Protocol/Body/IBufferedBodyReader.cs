namespace GaudiHTTP.Protocol.Body;

internal interface IBufferedBodyReader : IBodyReader
{
    int Feed(ReadOnlySpan<byte> data);
    void MarkComplete();
    ReadOnlyMemory<byte> GetBody();
}
