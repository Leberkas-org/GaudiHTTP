using System.Buffers;

namespace GaudiHTTP.Pooling;

internal sealed class CachedSegmentMemoryPool : MemoryPool<byte>
{
    private byte[]? _cached;
    private bool _rented;

    public override int MaxBufferSize => 1024 * 1024;

    public override IMemoryOwner<byte> Rent(int minBufferSize = -1)
    {
        var size = minBufferSize <= 0 ? 4096 : minBufferSize;

        if (!_rented && _cached is not null && _cached.Length >= size)
        {
            _rented = true;
            return new CachedOwner(this, _cached);
        }

        _rented = true;
        return new CachedOwner(this, new byte[Math.Max(size, 4096)]);
    }

    private void Return(byte[] array)
    {
        _rented = false;
        _cached = array;
    }

    protected override void Dispose(bool disposing)
    {
        _cached = null;
    }

    private sealed class CachedOwner : IMemoryOwner<byte>
    {
        private readonly CachedSegmentMemoryPool _pool;
        private byte[]? _array;

        public CachedOwner(CachedSegmentMemoryPool pool, byte[] array)
        {
            _pool = pool;
            _array = array;
        }

        public Memory<byte> Memory => _array.AsMemory();

        public void Dispose()
        {
            var arr = Interlocked.Exchange(ref _array, null);
            if (arr is not null)
            {
                _pool.Return(arr);
            }
        }
    }
}
