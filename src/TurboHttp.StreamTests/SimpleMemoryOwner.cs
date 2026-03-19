using System.Buffers;

namespace TurboHttp.StreamTests;

/// <summary>
/// Lightweight <see cref="System.Buffers.IMemoryOwner{T}"/> wrapper for test byte arrays.
/// Allows test data to be passed as managed buffers without allocating a MemoryPool segment.
/// </summary>
/// <remarks>
/// Dispose is a no-op; the backing array is held by the caller and subject to normal GC.
/// </remarks>
internal sealed class SimpleMemoryOwner(byte[] data) : IMemoryOwner<byte>
{
    public Memory<byte> Memory { get; } = data;

    public void Dispose()
    {
    }
}