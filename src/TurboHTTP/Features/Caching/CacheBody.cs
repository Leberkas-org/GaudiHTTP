using System.Buffers;

namespace TurboHTTP.Features.Caching;

public sealed class CacheBody : IDisposable
{
    private IMemoryOwner<byte>? _owner;

    internal CacheBody(IMemoryOwner<byte> owner, int length)
    {
        _owner = owner;
        Length = length;
    }

    public ReadOnlySpan<byte> Span => _owner is not null ? _owner.Memory.Span[..Length] : [];

    public ReadOnlyMemory<byte> Memory => _owner?.Memory[..Length] ?? ReadOnlyMemory<byte>.Empty;

    public int Length { get; }

    public bool IsEmpty => Length == 0;

    public void Dispose()
    {
        Interlocked.Exchange(ref _owner, null)?.Dispose();
    }
}