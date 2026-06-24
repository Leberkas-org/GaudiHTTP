using GaudiHTTP.Protocol.Body;

namespace GaudiHTTP.Tests.Protocol.Body;

/// <summary>
/// QueuedBodyReader is fed from the connection-stage thread while the application
/// thread consumes the body stream — a genuine cross-thread boundary. These specs
/// hammer that boundary: any lost, duplicated, or reordered chunk breaks the
/// position-derived byte pattern or the total length.
/// </summary>
public sealed class QueuedBodyReaderConcurrencySpec
{
    private const int ChunkSize = 64;
    private const int ChunkCount = 2000;

    private static byte[] BuildPattern()
    {
        var data = new byte[ChunkCount * ChunkSize];
        for (var p = 0; p < data.Length; p++)
        {
            data[p] = (byte)(p % 251);
        }

        return data;
    }

    private static async Task RunRoundAsync(bool throttleProducer, int round, CancellationToken ct)
    {
        var reader = new QueuedBodyReader(8);
        var expected = BuildPattern();

        var producer = Task.Run(() =>
        {
            for (var i = 0; i < ChunkCount; i++)
            {
                reader.TryEnqueue(expected.AsSpan(i * ChunkSize, ChunkSize));
                if (throttleProducer)
                {
                    // Keep the queue near-empty so the consumer's pending-read path
                    // (direct delivery) is exercised on almost every chunk.
                    Thread.SpinWait(200);
                }
            }

            reader.Complete();
        }, ct);

        using var ms = new MemoryStream();
        var stream = reader.AsStream();
        // Odd buffer size: forces partial chunk consumption between AdvanceTo calls.
        var buffer = new byte[48];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            ms.Write(buffer, 0, read);
        }

        await producer;

        var actual = ms.ToArray();
        Assert.True(expected.Length == actual.Length,
            $"round {round}: expected {expected.Length} bytes, got {actual.Length} (lost or duplicated chunks)");

        for (var p = 0; p < actual.Length; p++)
        {
            if (actual[p] != expected[p])
            {
                Assert.Fail($"round {round}: byte mismatch at position {p} (reordered or corrupted chunk)");
            }
        }
    }

    [Fact(Timeout = 30000)]
    public async Task Concurrent_enqueue_with_slow_producer_should_preserve_order_and_completeness()
    {
        for (var round = 0; round < 10; round++)
        {
            await RunRoundAsync(throttleProducer: true, round, TestContext.Current.CancellationToken);
        }
    }

    [Fact(Timeout = 30000)]
    public async Task Concurrent_enqueue_with_fast_producer_should_preserve_order_and_completeness()
    {
        for (var round = 0; round < 10; round++)
        {
            await RunRoundAsync(throttleProducer: false, round, TestContext.Current.CancellationToken);
        }
    }
}
