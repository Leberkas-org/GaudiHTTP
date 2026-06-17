using System.Runtime.CompilerServices;
using System.Text;

namespace TurboHTTP.Protocol;

/// <summary>Per-connection cache that maps UTF-8 header name bytes to interned strings via FNV-1a hashing.</summary>
internal sealed class HeaderNameCache
{
    private const int SlotCount = 128;
    private const int SlotMask = SlotCount - 1;
    private const ulong FnvOffsetBasis = 14695981039346656037;
    private const ulong FnvPrime = 1099511628211;

    private readonly (ulong Hash, int Length, string? Value)[] _slots = new (ulong, int, string?)[SlotCount];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string GetOrAdd(ReadOnlySpan<byte> utf8)
    {
        if (WellKnownHeaders.TryResolve(utf8, out var known))
        {
            return known;
        }

        var hash = ComputeHash(utf8);
        var idx = (int)(hash & SlotMask);
        ref var slot = ref _slots[idx];

        if (slot.Hash == hash && slot.Length == utf8.Length && slot.Value is not null)
        {
            return slot.Value;
        }

        var value = Encoding.UTF8.GetString(utf8);
        slot = (hash, utf8.Length, value);
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string GetOrAddAscii(ReadOnlySpan<byte> ascii)
    {
        if (WellKnownHeaders.TryResolve(ascii, out var known))
        {
            return known;
        }

        var hash = ComputeHash(ascii);
        var idx = (int)(hash & SlotMask);
        ref var slot = ref _slots[idx];

        if (slot.Hash == hash && slot.Length == ascii.Length && slot.Value is not null)
        {
            return slot.Value;
        }

        var value = Encoding.ASCII.GetString(ascii);
        slot = (hash, ascii.Length, value);
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong ComputeHash(ReadOnlySpan<byte> data)
    {
        var hash = FnvOffsetBasis;
        for (var i = 0; i < data.Length; i++)
        {
            hash ^= data[i];
            hash *= FnvPrime;
        }
        return hash;
    }
}
