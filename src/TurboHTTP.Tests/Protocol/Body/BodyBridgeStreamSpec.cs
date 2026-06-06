using TurboHTTP.Protocol.Body;

namespace TurboHTTP.Tests.Protocol.Body;

public sealed class BodyBridgeStreamSpec
{
    [Fact(Timeout = 5000)]
    public async Task ReadAsync_should_return_supplied_bytes()
    {
        var reader = new BridgedBodyReader();
        reader.Reset();
        var stream = reader.AsStream();

        reader.Supply("hello"u8.ToArray().AsMemory(), () => { });

        var buffer = new byte[16];
        var read = await stream.ReadAsync(buffer, TestContext.Current.CancellationToken);

        Assert.Equal(5, read);
        Assert.Equal("hello"u8.ToArray(), buffer[..5]);
    }

    [Fact(Timeout = 5000)]
    public async Task ReadAsync_should_return_zero_after_complete()
    {
        var reader = new BridgedBodyReader();
        reader.Reset();
        var stream = reader.AsStream();

        reader.Complete();

        var buffer = new byte[16];
        var read = await stream.ReadAsync(buffer, TestContext.Current.CancellationToken);

        Assert.Equal(0, read);
    }

    [Fact(Timeout = 5000)]
    public async Task ReadAsync_should_read_multiple_segments_sequentially()
    {
        var reader = new BridgedBodyReader();
        reader.Reset();
        var stream = reader.AsStream();

        var supplyCount = 0;
        reader.Supply("ab"u8.ToArray().AsMemory(), () =>
        {
            supplyCount++;
            if (supplyCount == 1)
            {
                reader.Supply("cd"u8.ToArray().AsMemory(), () =>
                {
                    reader.Complete();
                });
            }
        });

        var buffer = new byte[16];
        var total = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(total), TestContext.Current.CancellationToken)) > 0)
        {
            total += read;
        }

        Assert.Equal(4, total);
        Assert.Equal("abcd"u8.ToArray(), buffer[..4]);
    }
}
