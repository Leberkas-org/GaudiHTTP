using GaudiHTTP.Protocol.Body;

namespace GaudiHTTP.Tests.Protocol.Body;

public sealed class QueuedBodyReaderSpec
{
    [Fact(Timeout = 5000)]
    public async Task ReadAsync_should_return_enqueued_data()
    {
        var reader = new QueuedBodyReader(4);
        reader.TryEnqueue("hello"u8);

        var result = await reader.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("hello"u8.ToArray(), result.Memory.ToArray());
        Assert.False(result.IsCompleted);
    }

    [Fact(Timeout = 5000)]
    public async Task AdvanceTo_should_return_rental_and_allow_next_read()
    {
        var reader = new QueuedBodyReader(4);

        reader.TryEnqueue("first"u8);
        var result1 = await reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal("first"u8.ToArray(), result1.Memory.ToArray());
        reader.AdvanceTo();

        reader.TryEnqueue("second"u8);
        var result2 = await reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal("second"u8.ToArray(), result2.Memory.ToArray());
        reader.AdvanceTo();
    }

    [Fact(Timeout = 5000)]
    public async Task Complete_should_signal_end_of_body()
    {
        var reader = new QueuedBodyReader(4);
        reader.Complete();

        var result = await reader.ReadAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsCompleted);
        Assert.True(result.Memory.IsEmpty);
    }

    [Fact(Timeout = 5000)]
    public async Task Fault_should_propagate_exception()
    {
        var reader = new QueuedBodyReader(4);
        reader.Fault(new InvalidOperationException("test fault"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());
    }

    [Fact(Timeout = 5000)]
    public async Task TryEnqueue_should_return_false_at_backpressure_threshold_but_still_store_data()
    {
        var reader = new QueuedBodyReader(2);

        Assert.True(reader.TryEnqueue("a"u8));
        Assert.False(reader.TryEnqueue("b"u8));
        Assert.True(reader.IsFull);

        var r1 = await reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal("a"u8.ToArray(), r1.Memory.ToArray());
        reader.AdvanceTo();

        var r2 = await reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal("b"u8.ToArray(), r2.Memory.ToArray());
        reader.AdvanceTo();

        reader.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task SlotFreed_should_fire_after_AdvanceTo()
    {
        var reader = new QueuedBodyReader(4);
        var fired = false;
        reader.SlotFreed += () => fired = true;

        reader.TryEnqueue("data"u8);
        await reader.ReadAsync(TestContext.Current.CancellationToken);
        reader.AdvanceTo();

        Assert.True(fired);
    }

    [Fact(Timeout = 5000)]
    public async Task ReadAsync_should_wait_for_enqueue_when_empty()
    {
        var reader = new QueuedBodyReader(4);

        var readTask = reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.False(readTask.IsCompleted);

        reader.TryEnqueue("delayed"u8);

        var result = await readTask;
        Assert.Equal("delayed"u8.ToArray(), result.Memory.ToArray());
    }

    [Fact(Timeout = 5000)]
    public void IsBuffered_should_be_false()
    {
        var reader = new QueuedBodyReader(4);
        Assert.False(reader.IsBuffered);
    }

    [Fact(Timeout = 5000)]
    public async Task Reset_should_drain_and_allow_reuse()
    {
        var reader = new QueuedBodyReader(4);
        reader.TryEnqueue("old"u8);
        reader.Complete();
        reader.Reset();

        reader.TryEnqueue("new"u8);
        var result = await reader.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("new"u8.ToArray(), result.Memory.ToArray());
        Assert.False(result.IsCompleted);
    }

    [Fact(Timeout = 5000)]
    public async Task Complete_after_enqueue_should_deliver_data_then_completion()
    {
        var reader = new QueuedBodyReader(4);
        reader.TryEnqueue("data"u8);
        reader.Complete();

        var result1 = await reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal("data"u8.ToArray(), result1.Memory.ToArray());
        Assert.False(result1.IsCompleted);
        reader.AdvanceTo();

        var result2 = await reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.True(result2.IsCompleted);
        Assert.True(result2.Memory.IsEmpty);
    }

    [Fact(Timeout = 5000)]
    public async Task Multiple_chunks_should_be_readable_in_order()
    {
        var reader = new QueuedBodyReader(4);
        reader.TryEnqueue("one"u8);
        reader.TryEnqueue("two"u8);
        reader.TryEnqueue("three"u8);

        var r1 = await reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal("one"u8.ToArray(), r1.Memory.ToArray());
        reader.AdvanceTo();

        var r2 = await reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal("two"u8.ToArray(), r2.Memory.ToArray());
        reader.AdvanceTo();

        var r3 = await reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal("three"u8.ToArray(), r3.Memory.ToArray());
        reader.AdvanceTo();
    }

    [Fact(Timeout = 5000)]
    public async Task ReadAsync_should_throw_when_cancellation_token_fires()
    {
        var reader = new QueuedBodyReader(4);
        reader.Reset();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await reader.ReadAsync(cts.Token);
        });
    }

    [Fact(Timeout = 5000)]
    public async Task ReadAsync_should_throw_immediately_when_already_cancelled()
    {
        var reader = new QueuedBodyReader(4);
        reader.Reset();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await reader.ReadAsync(cts.Token);
        });
    }

    [Fact(Timeout = 5000)]
    public async Task ReadAsync_should_succeed_when_data_arrives_before_cancellation()
    {
        var reader = new QueuedBodyReader(4);
        reader.Reset();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var readTask = reader.ReadAsync(cts.Token);
        reader.TryEnqueue("hello"u8);

        var result = await readTask;
        Assert.Equal("hello"u8.ToArray(), result.Memory.ToArray());
        Assert.False(result.IsCompleted);
    }

    [Fact(Timeout = 5000)]
    public async Task Stale_cancellation_after_recycle_should_not_cancel_new_rentals_read()
    {
        // Regression: QueuedBodyReader is a Poolable rented per response body. ReadAsync
        // registered a cancel callback that captured only `this` — no version guard and never
        // disposed. After the reader was recycled and re-rented for an UNRELATED later request,
        // a stale token firing (owner A's operation cancelled/disposed after A finished) cancelled
        // owner B's in-flight body read. Mirror of the PendingRequest.TrySetCanceled fix.
        var reader = new QueuedBodyReader(4);

        // Owner A: an async read that registers a cancel callback bound to ctsA.
        using var ctsA = new CancellationTokenSource();
        var readA = reader.ReadAsync(ctsA.Token);
        Assert.False(readA.IsCompleted);

        // Owner A's body ends; consumer stops; reader is recycled back to the pool.
        reader.Complete();
        var resultA = await readA;
        Assert.True(resultA.IsCompleted);
        reader.Reset();

        // Owner B: re-rent and start a fresh read (rental version now bumped).
        using var ctsB = new CancellationTokenSource();
        var readB = reader.ReadAsync(ctsB.Token);
        Assert.False(readB.IsCompleted);

        // Fire the STALE token from owner A's rental.
        ctsA.Cancel();

        // Owner B's read must NOT be cancelled by the stale, unrelated token.
        Assert.False(readB.IsCompleted,
            "stale cancellation from a recycled rental corrupted the new rental's in-flight read");

        // And it still completes normally when owner B's data arrives.
        reader.TryEnqueue("live"u8);
        var resultB = await readB;
        Assert.Equal("live"u8.ToArray(), resultB.Memory.ToArray());
        Assert.False(resultB.IsCompleted);
    }

    [Fact(Timeout = 5000)]
    public async Task Cancellation_of_the_current_read_should_still_cancel_it()
    {
        // Guard must not over-reject: a legitimate in-time cancel of the CURRENT read still fires.
        var reader = new QueuedBodyReader(4);

        using var cts = new CancellationTokenSource();
        var readTask = reader.ReadAsync(cts.Token);
        Assert.False(readTask.IsCompleted);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await readTask);
    }

    [Fact(Timeout = 5000)]
    public async Task Reset_should_not_return_a_checked_out_rental_to_the_pool()
    {
        // Regression: on connection teardown/abort, Reset/Dispose ran while a consumer was
        // still reading the current chunk and returned its rental to the shared ArrayPool.
        // A concurrent stream then re-rented and overwrote it — correct-length, wrong-content
        // corruption. The checked-out rental must be returned only by the consumer's AdvanceTo.
        var pool = new TrackingArrayPool();
        var reader = new QueuedBodyReader(4, pool);

        reader.TryEnqueue("payload"u8);
        var result = await reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal("payload"u8.ToArray(), result.Memory.ToArray());

        // Teardown while the consumer still holds result.Memory.
        reader.Dispose();
        Assert.Equal(0, pool.ReturnedCount);

        // The consumer finishes reading and advances: the rental is returned exactly once.
        reader.AdvanceTo();
        Assert.Equal(1, pool.ReturnedCount);
    }

    [Fact(Timeout = 5000)]
    public void Reset_should_return_queued_but_unread_rentals()
    {
        // Queued chunks were never handed to the consumer, so Reset must reclaim them
        // (no leak) — only the published _current chunk is left for the consumer.
        var pool = new TrackingArrayPool();
        var reader = new QueuedBodyReader(4, pool);

        reader.TryEnqueue("a"u8);
        reader.TryEnqueue("b"u8);
        Assert.Equal(2, pool.RentedCount);

        reader.Dispose();
        Assert.Equal(2, pool.ReturnedCount);
    }

    private sealed class TrackingArrayPool : System.Buffers.ArrayPool<byte>
    {
        private readonly System.Buffers.ArrayPool<byte> _inner = Shared;

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
