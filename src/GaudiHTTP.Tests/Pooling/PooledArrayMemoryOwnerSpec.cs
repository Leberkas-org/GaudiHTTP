using System.Buffers;
using System.Runtime.InteropServices;
using Servus.Akka.Transport;

namespace GaudiHTTP.Tests.Pooling;

public sealed class WireBufferSpec
{
    private static byte[] BackingArray(Memory<byte> memory)
    {
        Assert.True(MemoryMarshal.TryGetArray<byte>(memory, out var segment));
        return segment.Array!;
    }

    [Fact(Timeout = 5000)]
    public void Rent_returns_capacity_at_least_the_requested_size()
    {
        using var owner = WireBuffer.Rent(100);

        Assert.True(owner.Capacity >= 100);
    }

    [Fact(Timeout = 5000)]
    public void Dispose_returns_the_array_to_the_pool_for_reuse()
    {
        var owner1 = WireBuffer.Rent(1024);
        var array1 = BackingArray(owner1.FullMemory);
        owner1.Dispose();

        using var owner2 = WireBuffer.Rent(1024);

        Assert.Same(array1, BackingArray(owner2.FullMemory));
    }

    [Fact(Timeout = 5000)]
    public async Task Buffer_returned_on_another_thread_is_reused()
    {
        // This is the whole point of the cross-thread pool: a process-wide pool with global, locked
        // per-bucket stacks survives the connection-stage -> application thread hop, unlike the
        // per-core MemoryPool<byte>.Shared whose return lands on a different core's stack.
        var owner1 = WireBuffer.Rent(4096);
        var array1 = BackingArray(owner1.FullMemory);

        await Task.Run(() => owner1.Dispose(), TestContext.Current.CancellationToken);

        using var owner2 = WireBuffer.Rent(4096);

        Assert.Same(array1, BackingArray(owner2.FullMemory));
    }

    [Fact(Timeout = 5000)]
    public void Double_dispose_does_not_return_the_array_twice()
    {
        var owner = WireBuffer.Rent(512);
        owner.Dispose();
        owner.Dispose();

        // If the wrapper were re-returned to the pool twice, two rents could hand out the same
        // wrapper instance to two live renters (wrapper aliasing). WireBuffer guards against this
        // by logging a warning on the second dispose and NOT re-returning the wrapper.
        using var a = WireBuffer.Rent(512);
        using var b = WireBuffer.Rent(512);

        // Both should get distinct backing arrays from distinct wrapper instances.
        Assert.NotSame(BackingArray(a.FullMemory), BackingArray(b.FullMemory));
    }

    [Fact(Timeout = 5000)]
    public void Shared_pool_rent_returns_a_usable_owner()
    {
        using var owner = WireBuffer.Rent(2048);

        Assert.True(owner.Capacity >= 2048);
    }
}
