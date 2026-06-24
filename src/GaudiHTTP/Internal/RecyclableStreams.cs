using Microsoft.IO;

namespace GaudiHTTP.Internal;

/// <summary>
/// Shared <see cref="RecyclableMemoryStreamManager"/> singleton for reducing GC pressure
/// from temporary <see cref="System.IO.MemoryStream"/> allocations in hot paths.
/// All streams obtained via <see cref="Manager"/> must be disposed after use so their
/// backing buffers are returned to the pool.
/// </summary>
internal static class RecyclableStreams
{
    internal static readonly RecyclableMemoryStreamManager Manager = new(new RecyclableMemoryStreamManager.Options
    {
        BlockSize = 128 * 1024,
        LargeBufferMultiple = 1024 * 1024,
        MaximumBufferSize = 8 * 1024 * 1024,
        MaximumSmallPoolFreeBytes = 16 * 1024 * 1024,
        MaximumLargePoolFreeBytes = 32 * 1024 * 1024
    });
}
