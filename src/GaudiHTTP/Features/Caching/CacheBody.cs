using System.Buffers;

namespace GaudiHTTP.Features.Caching;

/// <summary>
/// A pooled byte buffer holding the body of a cached HTTP response.
/// Wraps a rented <see cref="IMemoryOwner{T}"/> and exposes read-only views.
/// Dispose to return the underlying buffer to the pool.
/// </summary>
public sealed class CacheBody : IDisposable
{
    private IMemoryOwner<byte>? _owner;

    internal CacheBody(IMemoryOwner<byte> owner, int length)
    {
        _owner = owner;
        Length = length;
    }

    /// <summary>Gets a read-only span over the cached body bytes. Returns an empty span after disposal.</summary>
    public ReadOnlySpan<byte> Span => _owner is not null ? _owner.Memory.Span[..Length] : [];

    /// <summary>Gets a read-only memory region over the cached body bytes. Returns <see cref="ReadOnlyMemory{T}.Empty"/> after disposal.</summary>
    public ReadOnlyMemory<byte> Memory => _owner?.Memory[..Length] ?? ReadOnlyMemory<byte>.Empty;

    /// <summary>Gets the number of valid bytes in the cached body.</summary>
    public int Length { get; }

    /// <summary>Gets a value indicating whether the cached body contains no bytes.</summary>
    public bool IsEmpty => Length == 0;

    /// <summary>Returns the underlying buffer to the pool. Subsequent accesses to <see cref="Span"/> and <see cref="Memory"/> return empty.</summary>
    public void Dispose()
    {
        Interlocked.Exchange(ref _owner, null)?.Dispose();
    }
}