using System.Buffers;

namespace TurboHTTP.Protocol.Body;

internal sealed class PassthroughFramingEncoder : IFramingEncoder
{
    public int Headroom => 0;
    public int Trailer => 0;

    public ReadOnlyMemory<byte> Frame(IMemoryOwner<byte> buffer, int headroom, int dataLength)
        => buffer.Memory[..dataLength];

    public OwnedMemory GetTerminator() => default;
}
