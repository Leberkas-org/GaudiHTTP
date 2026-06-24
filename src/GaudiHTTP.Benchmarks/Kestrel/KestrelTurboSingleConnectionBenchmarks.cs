using BenchmarkDotNet.Attributes;
using GaudiHTTP.Benchmarks.Internal;
using GaudiHTTP.Client;

namespace GaudiHTTP.Benchmarks.Kestrel;

/// <summary>
/// Concurrent light GETs over a SINGLE connection (MaxConnectionsPerServer = 1) for
/// <see cref="IGaudiHttpClient.SendAsync"/>. This isolates pure protocol multiplexing efficiency:
/// for H2/H3 it measures stream multiplexing + HPACK/QPACK + flow control on one connection (the
/// core multiplexing value proposition); for H1.1 it measures pipelining/serialization on one
/// connection. The standard concurrent suite hides this by spreading load across many connections.
/// </summary>
[MemoryDiagnoser]
[WarmupCount(3)]
[IterationCount(10)]
public class KestrelTurboSingleConnectionBenchmarks : KestrelBaseClass
{
    [Params(64, 256)]
    public int ConcurrencyLevel { get; set; }

    private ClientHelper _clientHelper = null!;
    private Task[] _tasks = null!;

    [GlobalSetup]
    public override async Task GlobalSetup()
    {
        await base.GlobalSetup();
        _clientHelper = ClientHelper.CreateClient(BaseAddress, HttpVersionValue, maxConnectionsOverride: 1);
        _tasks = new Task[ConcurrencyLevel];
        await WarmupRequest();
    }

    [GlobalCleanup]
    public override async Task GlobalCleanup()
    {
        await _clientHelper.DisposeAsync();
        await base.GlobalCleanup();
    }

    /// <inheritdoc />
    public override async Task WarmupRequest()
    {
        await SendLight();
    }

    [Benchmark]
    public Task ConcurrentRequests_SingleConnection()
    {
        for (var i = 0; i < ConcurrencyLevel; i++)
        {
            _tasks[i] = SendLight();
        }

        return Task.WhenAll(_tasks).WaitAsync(TimeSpan.FromSeconds(60));
    }

    private async Task SendLight()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LightUri);
        using var response = await _clientHelper.Client.SendAsync(request, CancellationToken.None);
        response.EnsureSuccessStatusCode();
    }
}
