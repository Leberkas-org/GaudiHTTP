using TurboHTTP.Protocol.Body;

namespace TurboHTTP.Tests.Protocol.Body;

public sealed class BridgedBodyReaderSpec
{
    [Fact(Timeout = 5000)]
    public async Task ReadAsync_should_return_supplied_segment()
    {
        var reader = new BridgedBodyReader();
        reader.Reset();

        var data = "hello"u8.ToArray().AsMemory();
        reader.Supply(data, onConsumed: () => { });

        var result = await reader.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(data.ToArray(), result.Memory.ToArray());
        Assert.False(result.IsCompleted);
    }

    [Fact(Timeout = 5000)]
    public async Task AdvanceTo_should_invoke_onConsumed_callback()
    {
        var reader = new BridgedBodyReader();
        reader.Reset();

        var callbackInvoked = false;
        reader.Supply("data"u8.ToArray().AsMemory(), onConsumed: () => callbackInvoked = true);

        await reader.ReadAsync(TestContext.Current.CancellationToken);
        reader.AdvanceTo(4);

        Assert.True(callbackInvoked);
    }

    [Fact(Timeout = 5000)]
    public async Task Complete_should_signal_end_of_body()
    {
        var reader = new BridgedBodyReader();
        reader.Reset();

        reader.Complete();

        var result = await reader.ReadAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsCompleted);
        Assert.True(result.Memory.IsEmpty);
        Assert.True(reader.IsCompleted);
    }

    [Fact(Timeout = 5000)]
    public async Task Fault_should_propagate_exception_to_reader()
    {
        var reader = new BridgedBodyReader();
        reader.Reset();

        reader.Fault(new InvalidOperationException("test error"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await reader.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    public async Task Supply_then_read_then_advance_should_allow_next_supply()
    {
        var reader = new BridgedBodyReader();
        reader.Reset();

        var advancedCount = 0;

        reader.Supply("ab"u8.ToArray().AsMemory(), () => advancedCount++);
        var r1 = await reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, r1.Memory.Length);
        reader.AdvanceTo(2);
        Assert.Equal(1, advancedCount);

        reader.Supply("cd"u8.ToArray().AsMemory(), () => advancedCount++);
        var r2 = await reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, r2.Memory.Length);
        reader.AdvanceTo(2);
        Assert.Equal(2, advancedCount);
    }

    [Fact(Timeout = 5000)]
    public void IsBuffered_should_be_false()
    {
        var reader = new BridgedBodyReader();
        Assert.False(reader.IsBuffered);
    }

    [Fact(Timeout = 5000)]
    public void GetBufferedBody_should_throw()
    {
        var reader = new BridgedBodyReader();
        Assert.Throws<NotSupportedException>(() => reader.GetBufferedBody());
    }

    [Fact(Timeout = 5000)]
    public async Task Reset_should_allow_reuse()
    {
        var reader = new BridgedBodyReader();
        reader.Reset();
        reader.Complete();
        var r1 = await reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.True(r1.IsCompleted);

        reader.Reset();
        Assert.False(reader.IsCompleted);

        reader.Supply("new"u8.ToArray().AsMemory(), () => { });
        var r2 = await reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, r2.Memory.Length);
    }
}
