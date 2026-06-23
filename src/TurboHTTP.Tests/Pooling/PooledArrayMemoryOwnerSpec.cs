using System.Buffers;
using System.Runtime.InteropServices;
using TurboHTTP.Pooling;

namespace TurboHTTP.Tests.Pooling;

public sealed class PooledArrayMemoryOwnerSpec
{
    private static byte[] BackingArray(Memory<byte> memory)
    {
        Assert.True(MemoryMarshal.TryGetArray<byte>(memory, out var segment));
        return segment.Array!;
    }

    [Fact(Timeout = 5000)]
    public void Memory_is_at_least_the_requested_length()
    {
        var pool = ArrayPool<byte>.Create();
        using var owner = new PooledArrayMemoryOwner(pool, 100);

        Assert.True(owner.Memory.Length >= 100);
    }

    [Fact(Timeout = 5000)]
    public void Dispose_returns_the_array_to_the_pool_for_reuse()
    {
        var pool = ArrayPool<byte>.Create();
        var owner1 = new PooledArrayMemoryOwner(pool, 1024);
        var array1 = BackingArray(owner1.Memory);
        owner1.Dispose();

        using var owner2 = new PooledArrayMemoryOwner(pool, 1024);

        Assert.Same(array1, BackingArray(owner2.Memory));
    }

    [Fact(Timeout = 5000)]
    public async Task Buffer_returned_on_another_thread_is_reused()
    {
        // This is the whole point of #2: a process-wide pool with global, locked per-bucket
        // stacks survives the connection-stage -> application thread hop, unlike the per-core
        // MemoryPool<byte>.Shared whose return lands on a different core's stack.
        var pool = ArrayPool<byte>.Create();
        var owner1 = new PooledArrayMemoryOwner(pool, 4096);
        var array1 = BackingArray(owner1.Memory);

        await Task.Run(() => owner1.Dispose(), TestContext.Current.CancellationToken);

        using var owner2 = new PooledArrayMemoryOwner(pool, 4096);

        Assert.Same(array1, BackingArray(owner2.Memory));
    }

    [Fact(Timeout = 5000)]
    public void Double_dispose_does_not_return_the_array_twice()
    {
        var pool = ArrayPool<byte>.Create();
        var owner = new PooledArrayMemoryOwner(pool, 512);
        owner.Dispose();
        owner.Dispose();

        // If the array were returned twice, the bucket would hold it twice and two rents
        // could hand out the same buffer to two live owners.
        using var a = new PooledArrayMemoryOwner(pool, 512);
        using var b = new PooledArrayMemoryOwner(pool, 512);

        Assert.NotSame(BackingArray(a.Memory), BackingArray(b.Memory));
    }

    [Fact(Timeout = 5000)]
    public void Cross_thread_buffer_pool_rents_a_usable_owner()
    {
        using var owner = CrossThreadBufferPool.Rent(2048);

        Assert.True(owner.Memory.Length >= 2048);
    }
}
