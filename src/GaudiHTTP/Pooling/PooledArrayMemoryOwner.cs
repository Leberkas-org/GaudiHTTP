using System.Buffers;

namespace GaudiHTTP.Pooling;

// An IMemoryOwner backed by a rented array from a caller-supplied ArrayPool. Returns the array
// to that pool exactly once on Dispose. Used for body buffers that are rented on the connection-
// stage thread and disposed on the application thread, where a process-wide pool with global,
// locked per-bucket stacks keeps the rent/return hit rate intact across the thread hop.
internal sealed class PooledArrayMemoryOwner : IMemoryOwner<byte>
{
    private readonly ArrayPool<byte> _pool;
    private byte[]? _array;

    public PooledArrayMemoryOwner(ArrayPool<byte> pool, int minimumLength)
    {
        _pool = pool;
        _array = pool.Rent(minimumLength);
    }

    public Memory<byte> Memory
        => _array ?? throw new ObjectDisposedException(nameof(PooledArrayMemoryOwner));

    public void Dispose()
    {
        var array = _array;
        if (array is null)
        {
            return;
        }

        _array = null;
        _pool.Return(array);
    }
}
