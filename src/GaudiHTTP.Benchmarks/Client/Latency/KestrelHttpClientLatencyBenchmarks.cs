using BenchmarkDotNet.Attributes;
using GaudiHTTP.Benchmarks.Internal;

namespace GaudiHTTP.Benchmarks.Client.Latency;

/// <summary>
/// Baseline single-request latency for .NET <see cref="HttpClient"/> (SocketsHttpHandler) against
/// a localhost Kestrel server. Mirrors <see cref="KestrelGaudiLatencyBenchmarks"/> exactly so the
/// per-request round-trips are directly comparable.
/// </summary>
[WarmupCount(5)]
[IterationCount(15)]
public class KestrelHttpClientLatencyBenchmarks : KestrelBaseClass
{
    private const int Ops = 32;

    private HttpClient _httpClient = null!;

    [GlobalSetup]
    public override async Task GlobalSetup()
    {
        await base.GlobalSetup();
        _httpClient = CreateBaselineHttpClient();
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
        using var response = await _httpClient.GetAsync(LightUri);
        response.EnsureSuccessStatusCode();
    }

    [Benchmark(OperationsPerInvoke = Ops)]
    public async Task SingleRequestLatency()
    {
        for (var i = 0; i < Ops; i++)
        {
            using var response = await _httpClient.GetAsync(LightUri);
            response.EnsureSuccessStatusCode();
        }
    }
}
