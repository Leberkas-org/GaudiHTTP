using System.Buffers;
using GaudiHTTP.Protocol.Body;

namespace GaudiHTTP.Tests.Protocol.Body;

/// <summary>
/// Behaviour of the <see cref="QueuedBodyReader.AsStream"/> wrapper, focused on CopyToAsync:
/// it writes pooled chunks straight to the destination (no intermediate framework buffer) while
/// preserving exact bytes, draining a prior partial read, and returning every rental exactly once.
/// </summary>
public sealed class QueuedBodyStreamSpec
{
    [Fact(Timeout = 5000)]
    public async Task CopyToAsync_should_copy_the_full_body_and_return_every_rental()
    {
        var pool = new TrackingArrayPool();
        var reader = new QueuedBodyReader(8, pool);
        reader.TryEnqueue("one"u8);
        reader.TryEnqueue("two"u8);
        reader.TryEnqueue("three"u8);
        reader.Complete();

        using var destination = new MemoryStream();
        await reader.AsStream().CopyToAsync(destination, TestContext.Current.CancellationToken);

        Assert.Equal("onetwothree"u8.ToArray(), destination.ToArray());
        Assert.Equal(pool.RentedCount, pool.ReturnedCount);
        Assert.Equal(3, pool.RentedCount);
    }

    [Fact(Timeout = 5000)]
    public async Task CopyToAsync_should_copy_the_remainder_after_a_partial_read()
    {
        var pool = new TrackingArrayPool();
        var reader = new QueuedBodyReader(8, pool);
        reader.TryEnqueue("abcdef"u8);
        reader.TryEnqueue("ghij"u8);
        reader.Complete();

        var stream = reader.AsStream();

        var head = new byte[2];
        var read = await stream.ReadAsync(head, TestContext.Current.CancellationToken);
        Assert.Equal(2, read);
        Assert.Equal("ab"u8.ToArray(), head);

        using var destination = new MemoryStream();
        await stream.CopyToAsync(destination, TestContext.Current.CancellationToken);

        // Exactly the bytes after the partial read — no dropped or duplicated bytes.
        Assert.Equal("cdefghij"u8.ToArray(), destination.ToArray());
        Assert.Equal(pool.RentedCount, pool.ReturnedCount);
    }

    [Fact(Timeout = 5000)]
    public async Task CopyToAsync_should_write_nothing_when_the_body_is_already_consumed()
    {
        var reader = new QueuedBodyReader(8);
        reader.TryEnqueue("payload"u8);
        reader.Complete();

        var stream = reader.AsStream();
        using var first = new MemoryStream();
        await stream.CopyToAsync(first, TestContext.Current.CancellationToken);
        Assert.Equal("payload"u8.ToArray(), first.ToArray());

        using var second = new MemoryStream();
        await stream.CopyToAsync(second, TestContext.Current.CancellationToken);
        Assert.Empty(second.ToArray());
    }

    [Fact(Timeout = 5000)]
    public async Task CopyToAsync_should_write_nothing_for_an_empty_body()
    {
        var reader = new QueuedBodyReader(8);
        reader.Complete();

        using var destination = new MemoryStream();
        await reader.AsStream().CopyToAsync(destination, TestContext.Current.CancellationToken);

        Assert.Empty(destination.ToArray());
    }

    [Fact(Timeout = 5000)]
    public void Dispose_without_reading_should_invoke_onAbandoned_callback()
    {
        var callbackFired = false;
        var reader = new QueuedBodyReader(capacity: 4);
        reader.TryEnqueue(new byte[] { 1, 2, 3 });
        reader.Complete();

        var stream = new QueuedBodyStream(reader, onAbandoned: () => callbackFired = true);
        stream.Dispose();

        Assert.True(callbackFired);
    }

    [Fact(Timeout = 5000)]
    public async Task Dispose_after_fully_reading_should_not_invoke_callback()
    {
        var callbackFired = false;
        var reader = new QueuedBodyReader(capacity: 4);
        reader.TryEnqueue(new byte[] { 1, 2, 3 });
        reader.Complete();

        var stream = new QueuedBodyStream(reader, onAbandoned: () => callbackFired = true);
        var buf = new byte[64];
        while (await stream.ReadAsync(buf, TestContext.Current.CancellationToken) > 0) { }
        stream.Dispose();

        Assert.False(callbackFired);
    }

    [Fact(Timeout = 5000)]
    public void Dispose_without_callback_should_not_throw()
    {
        var reader = new QueuedBodyReader(capacity: 4);
        reader.TryEnqueue(new byte[] { 1, 2, 3 });
        reader.Complete();

        var stream = new QueuedBodyStream(reader);
        stream.Dispose();
    }

    private sealed class TrackingArrayPool : ArrayPool<byte>
    {
        private readonly ArrayPool<byte> _inner = Shared;

        public int RentedCount { get; private set; }
        public int ReturnedCount { get; private set; }

        public override byte[] Rent(int minimumLength)
        {
            RentedCount++;
            return _inner.Rent(minimumLength);
        }

        public override void Return(byte[] array, bool clearArray = false)
        {
            ReturnedCount++;
            _inner.Return(array, clearArray);
        }
    }
}
