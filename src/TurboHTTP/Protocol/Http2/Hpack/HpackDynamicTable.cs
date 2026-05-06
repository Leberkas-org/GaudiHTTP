using System.Text;

namespace TurboHTTP.Protocol.Http2.Hpack;

/// <summary>
/// RFC 7541 §4.1 - Dynamic Table.
/// FIFO queue: newest entries at the front, oldest evicted on overflow.
/// Each entry costs: Name.Length + Value.Length + 32 bytes overhead (RFC 7541 §4.1).
/// Both the name byte length and total encoded size are computed once at insertion
/// and cached to avoid repeated <see cref="System.Text.Encoding.UTF8"/> GetByteCount
/// calls during eviction, header-list-size accounting, and name-reference lookups.
/// </summary>
internal sealed class HpackDynamicTable
{
    private (HpackHeader Header, int NameByteLength, int EncodedSize)[] _ring;
    private int _head;
    private int _count;

    private readonly Dictionary<string, int> _nameIndex = new(StringComparer.OrdinalIgnoreCase);

    private int _evictedCount;

    public HpackDynamicTable() : this(16) { }

    private HpackDynamicTable(int initialCapacity)
    {
        _ring = new (HpackHeader, int, int)[initialCapacity];
    }

    /// <summary>Currently configured maximum table size in bytes.</summary>
    public int MaxSize { get; private set; } = 4096;

    /// <summary>Currently occupied table size in bytes.</summary>
    public int CurrentSize { get; private set; }

    /// <summary>
    /// RFC 7541 §4.2 - Sets the maximum table size.
    /// Triggers eviction of oldest entries if the new limit is exceeded.
    /// </summary>
    public void SetMaxSize(int newMax)
    {
        if (newMax < 0)
        {
            throw new HpackException($"Invalid HPACK table size: {newMax}");
        }

        MaxSize = newMax;
        Evict();
    }

    /// <summary>
    /// RFC 7541 §4.4 - Adds a new entry to the front of the table.
    /// If the entry alone exceeds MaxSize, the entire table is cleared.
    /// </summary>
    public void Add(string name, string value)
    {
        var nameByteLength = Encoding.UTF8.GetByteCount(name);
        var valueByteLength = Encoding.UTF8.GetByteCount(value);
        var entrySize = nameByteLength + valueByteLength + 32;

        if (entrySize > MaxSize)
        {
            Clear();
            return;
        }

        if (_count == _ring.Length)
        {
            Grow();
        }

        var absolutePos = _evictedCount + _count;
        var index = (_head + _count) % _ring.Length;
        _ring[index] = (new HpackHeader(name, value), nameByteLength, entrySize);
        _count++;
        _nameIndex[name] = absolutePos;
        CurrentSize += entrySize;
        Evict();
    }

    /// <summary>
    /// RFC 7541 §2.3.3 - Dynamic index is 1-based (relative to the table).
    /// Index 1 = most recently added entry.
    /// </summary>
    public HpackHeader? GetEntry(int dynamicIndex)
    {
        if (dynamicIndex <= 0 || dynamicIndex > _count)
        {
            return null;
        }

        var listIndex = _count - dynamicIndex;
        var ringIndex = (_head + listIndex) % _ring.Length;
        return _ring[ringIndex].Header;
    }

    /// <summary>
    /// Returns the header, its pre-computed name byte length, and total encoded entry size
    /// for the given 1-based dynamic index, or null if out of range.
    /// </summary>
    public (HpackHeader Header, int NameByteLength, int EncodedSize)? GetEntryWithSizes(int dynamicIndex)
    {
        if (dynamicIndex <= 0 || dynamicIndex > _count)
        {
            return null;
        }

        var listIndex = _count - dynamicIndex;
        var ringIndex = (_head + listIndex) % _ring.Length;
        var entry = _ring[ringIndex];
        return (entry.Header, entry.NameByteLength, entry.EncodedSize);
    }

    /// <summary>Number of entries currently in the dynamic table.</summary>
    public int Count => _count;

    /// <summary>
    /// O(1) lookup: finds the 1-based dynamic index for a full (name+value) match.
    /// Returns 0 if not found.
    /// </summary>
    public int FindFullMatch(string name, string value)
    {
        if (!_nameIndex.TryGetValue(name, out var absolutePos))
        {
            return 0;
        }

        var listIndex = absolutePos - _evictedCount;
        if (listIndex < 0 || listIndex >= _count)
        {
            return 0;
        }

        var ringIndex = (_head + listIndex) % _ring.Length;
        var entry = _ring[ringIndex];
        if (string.Equals(entry.Header.Value, value, StringComparison.Ordinal))
        {
            return _count - listIndex;
        }

        for (var i = _count - 1; i >= 0; i--)
        {
            var ri = (_head + i) % _ring.Length;
            var e = _ring[ri];
            if (string.Equals(e.Header.Name, name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.Header.Value, value, StringComparison.Ordinal))
            {
                return _count - i;
            }
        }

        return 0;
    }

    /// <summary>
    /// O(1) lookup: finds the 1-based dynamic index for a name-only match.
    /// Returns 0 if not found.
    /// </summary>
    public int FindNameMatch(string name)
    {
        if (!_nameIndex.TryGetValue(name, out var absolutePos))
        {
            return 0;
        }

        var listIndex = absolutePos - _evictedCount;
        if (listIndex < 0 || listIndex >= _count)
        {
            return 0;
        }

        return _count - listIndex;
    }

    private void Evict()
    {
        while (CurrentSize > MaxSize && _count > 0)
        {
            var oldest = _ring[_head];
            CurrentSize -= oldest.EncodedSize;

            if (_nameIndex.TryGetValue(oldest.Header.Name, out var pos) && pos == _evictedCount)
            {
                _nameIndex.Remove(oldest.Header.Name);
            }

            _ring[_head] = default;
            _head = (_head + 1) % _ring.Length;
            _count--;
            _evictedCount++;
        }
    }

    private void Clear()
    {
        Array.Clear(_ring, 0, _ring.Length);
        _head = 0;
        _count = 0;
        _nameIndex.Clear();
        CurrentSize = 0;
    }

    private void Grow()
    {
        var newCapacity = _ring.Length * 2;
        var newRing = new (HpackHeader, int, int)[newCapacity];

        for (var i = 0; i < _count; i++)
        {
            newRing[i] = _ring[(_head + i) % _ring.Length];
        }

        _ring = newRing;
        _head = 0;
    }
}
