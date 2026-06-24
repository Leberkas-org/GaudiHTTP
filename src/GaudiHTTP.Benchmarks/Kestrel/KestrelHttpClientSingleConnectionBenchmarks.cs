using BenchmarkDotNet.Attributes;
using GaudiHTTP.Benchmarks.Internal;

namespace GaudiHTTP.Benchmarks.Kestrel;

/// <summary>
/// Baseline concurrent light GETs over a SINGLE connection (MaxConnectionsPerServer = 1) for
/// .NET <see cref="HttpClient"/> (SocketsHttpHandler). Mirrors
/// <see cref="KestrelGaudiSingleConnectionBenchmarks"/> so single-connection multiplexing
/// efficiency is directly comparable. EnableMultipleHttp2Connections is irrelevant here because
/// the per-server cap is pinned to 1.
/// </summary>
[MemoryDiagnoser]
[WarmupCount(3)]
[IterationCount(10)]
public class KestrelHttpClientSingleConnectionBenchmarks : KestrelBaseClass
{
    [Params(64, 256)]
    public int ConcurrencyLevel { get; set; }

    private HttpClient _httpClient = null!;
    private Task[] _tasks = null!;

    [GlobalSetup]
    public override async Task GlobalSetup()
    {
        await base.GlobalSetup();
        _httpClient = CreateBaselineHttpClient(maxConnectionsPerServer: 1);
        _tasks = new Task[ConcurrencyLevel];
        await WarmupRequest();
    }

    [GlobalCleanup]
    public override async Task GlobalCleanup()
    {
        _httpClient.Dispose();
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
        using var response = await _httpClient.GetAsync(LightUri);
        response.EnsureSuccessStatusCode();
    }
}
