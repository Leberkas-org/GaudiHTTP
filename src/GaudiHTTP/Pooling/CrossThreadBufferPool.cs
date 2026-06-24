using System.Buffers;

namespace GaudiHTTP.Pooling;

// One process-wide pool for body buffers that cross the connection-stage -> application thread
// boundary. ArrayPool<byte>.Create uses global, locked per-bucket stacks (no per-core affinity),
// so a buffer rented on the stage thread and returned on the app thread is reused instead of
// missing the pool and forcing a fresh allocation (the failure mode of MemoryPool<byte>.Shared /
// the per-core ArrayPool<byte>.Shared). Shared by QueuedBodyReader and BufferedBodyReader.
internal static class CrossThreadBufferPool
{
    internal static readonly ArrayPool<byte> Shared =
        ArrayPool<byte>.Create(maxArrayLength: 1024 * 1024, maxArraysPerBucket: 512);

    public static IMemoryOwner<byte> Rent(int minimumLength)
        => new PooledArrayMemoryOwner(Shared, minimumLength);
}
