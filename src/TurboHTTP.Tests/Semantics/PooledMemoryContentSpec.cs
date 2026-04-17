using System.Buffers;
using TurboHTTP.Internal;

namespace TurboHTTP.Tests.Semantics;

public sealed class PooledBodyContentSpec
{
    [Fact(Timeout = 5000)]
    public async Task SerializeToStreamAsync_should_write_correct_bytes()
    {
        var data = "hello world"u8.ToArray();
        var owner = MemoryPool<byte>.Shared.Rent(data.Length);
        data.CopyTo(owner.Memory.Span);

        using var content = new PooledBodyContent(owner, data.Length);
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms);

        Assert.Equal(data, ms.ToArray());
    }

    [Fact(Timeout = 5000)]
    public void SerializeToStream_should_write_correct_bytes()
    {
        var data = "hello world"u8.ToArray();
        var owner = MemoryPool<byte>.Shared.Rent(data.Length);
        data.CopyTo(owner.Memory.Span);

        using var content = new PooledBodyContent(owner, data.Length);
        using var ms = new MemoryStream();
        content.CopyTo(ms, null, CancellationToken.None);

        Assert.Equal(data, ms.ToArray());
    }

    [Fact(Timeout = 5000)]
    public void Serialize_after_dispose_should_throw_ObjectDisposedException()
    {
        var owner = MemoryPool<byte>.Shared.Rent(16);
        var content = new PooledBodyContent(owner, 16);
        content.Dispose();

        using var ms = new MemoryStream();
        Assert.Throws<ObjectDisposedException>(() => content.CopyTo(ms, null, CancellationToken.None));
    }

    [Fact(Timeout = 5000)]
    public async Task SerializeAsync_after_dispose_should_throw_ObjectDisposedException()
    {
        var owner = MemoryPool<byte>.Shared.Rent(16);
        var content = new PooledBodyContent(owner, 16);
        content.Dispose();

        using var ms = new MemoryStream();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => content.CopyToAsync(ms));
    }

    [Fact(Timeout = 5000)]
    public void Double_dispose_should_not_throw()
    {
        var owner = MemoryPool<byte>.Shared.Rent(16);
        var content = new PooledBodyContent(owner, 16);
        content.Dispose();
        content.Dispose();
    }

    [Fact(Timeout = 10000)]
    public async Task Dispose_during_async_write_should_not_corrupt_data()
    {
        var data = new byte[4096];
        Random.Shared.NextBytes(data);
        var owner = MemoryPool<byte>.Shared.Rent(data.Length);
        data.CopyTo(owner.Memory.Span);

        var content = new PooledBodyContent(owner, data.Length);
        var gate = new SemaphoreSlim(0, 1);
        var slowStream = new GatedWriteStream(gate);

        var writeTask = content.CopyToAsync(slowStream);

        await slowStream.WaitUntilWriteStarted();

        content.Dispose();

        gate.Release();
        await writeTask;

        Assert.Equal(data, slowStream.WrittenBytes);
    }

    [Fact(Timeout = 10000)]
    public async Task Concurrent_serialize_and_dispose_should_not_crash()
    {
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var data = new byte[256];
            Random.Shared.NextBytes(data);
            var owner = MemoryPool<byte>.Shared.Rent(data.Length);
            data.CopyTo(owner.Memory.Span);

            var content = new PooledBodyContent(owner, data.Length);

            var serializeTask = Task.Run(async () =>
            {
                try
                {
                    using var ms = new MemoryStream();
                    await content.CopyToAsync(ms);
                    return ms.ToArray();
                }
                catch (ObjectDisposedException)
                {
                    return null;
                }
                catch (HttpRequestException ex) when (ex.InnerException is ObjectDisposedException)
                {
                    return null;
                }
            });

            var disposeTask = Task.Run(() =>
            {
                content.Dispose();
            });

            await Task.WhenAll(serializeTask, disposeTask);

            var result = await serializeTask;
            if (result is not null)
            {
                Assert.Equal(data, result);
            }
        }
    }

    [Fact(Timeout = 5000)]
    public void TryComputeLength_should_return_exact_length()
    {
        var owner = MemoryPool<byte>.Shared.Rent(128);
        using var content = new PooledBodyContent(owner, 42);

        Assert.Equal(42, content.Headers.ContentLength);
    }

    private sealed class GatedWriteStream : MemoryStream
    {
        private readonly SemaphoreSlim _gate;
        private readonly TaskCompletionSource _writeStarted = new();

        public GatedWriteStream(SemaphoreSlim gate)
        {
            _gate = gate;
        }

        public byte[] WrittenBytes => ToArray();

        public Task WaitUntilWriteStarted() => _writeStarted.Task;

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _writeStarted.TrySetResult();
            await _gate.WaitAsync(cancellationToken);
            await base.WriteAsync(buffer, cancellationToken);
        }
    }
}
