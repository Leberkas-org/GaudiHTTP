using BenchmarkDotNet.Attributes;
using GaudiHTTP.Benchmarks.Internal;

namespace GaudiHTTP.Benchmarks.Kestrel;

/// <summary>
/// Baseline cold-start cost for .NET <see cref="HttpClient"/> (SocketsHttpHandler): each measured
/// invocation creates a fresh handler+client and performs its FIRST request against an already-warm
/// Kestrel server, capturing connection establishment (DNS/TCP/TLS/QUIC handshake). Mirrors
/// <see cref="KestrelGaudiColdStartBenchmarks"/>; created clients are stashed and disposed in cleanup
/// so teardown stays out of the measured region.
/// </summary>
[MemoryDiagnoser]
[WarmupCount(2)]
[IterationCount(8)]
[InvocationCount(1, 1)]
public class KestrelHttpClientColdStartBenchmarks : KestrelBaseClass
{
    private readonly List<HttpClient> _created = new();

    [GlobalSetup]
    public override Task GlobalSetup() => base.GlobalSetup();

    [GlobalCleanup]
    public override Task GlobalCleanup()
    {
        foreach (var client in _created)
        {
            client.Dispose();
        }

        _created.Clear();
        return base.GlobalCleanup();
    }

    [Benchmark]
    public async Task ColdStartFirstRequest()
    {
        var client = CreateBaselineHttpClient();
        _created.Add(client);

        using var response = await client.GetAsync(LightUri);
        response.EnsureSuccessStatusCode();
    }
}
