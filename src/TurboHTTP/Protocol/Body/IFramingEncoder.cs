using System.Buffers;

namespace TurboHTTP.Protocol.Body;

internal readonly struct OwnedMemory(IMemoryOwner<byte> owner, ReadOnlyMemory<byte> memory)
{
    public IMemoryOwner<byte> Owner { get; } = owner;
    public ReadOnlyMemory<byte> Memory { get; } = memory;
    public bool IsEmpty => Memory.IsEmpty;
}

internal interface IFramingEncoder
{
    int Headroom { get; }
    int Trailer { get; }
    ReadOnlyMemory<byte> Frame(IMemoryOwner<byte> buffer, int headroom, int dataLength);
    OwnedMemory GetTerminator();
}
