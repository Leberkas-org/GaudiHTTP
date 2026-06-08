using TurboHTTP.Protocol.Body;

namespace TurboHTTP.Tests.Protocol.Body;

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
}
