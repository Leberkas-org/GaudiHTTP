using TurboHTTP.Protocol;

namespace TurboHTTP.Tests.Protocol;

public sealed class BodyHandleSpec
{
    [Fact(Timeout = 5000)]
    public async Task ReadAsync_should_fault_when_body_exceeds_limit_instead_of_hanging()
    {
        using var handle = new BodyHandle(maxBodySize: 8);
        var stream = handle.AsStream();

        Assert.Throws<HttpProtocolException>(() => handle.Feed(new byte[16]));

        var buffer = new byte[16];
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await stream.ReadExactlyAsync(buffer, TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    public async Task ReadAsync_should_return_fed_bytes_then_zero_on_complete()
    {
        using var handle = new BodyHandle(maxBodySize: 1024);
        var stream = handle.AsStream();

        handle.Feed([1, 2, 3]);
        handle.Complete();

        var buffer = new byte[16];
        var read = await stream.ReadAsync(buffer, TestContext.Current.CancellationToken);
        Assert.Equal(3, read);
        Assert.Equal(0, await stream.ReadAsync(buffer, TestContext.Current.CancellationToken));
    }
}