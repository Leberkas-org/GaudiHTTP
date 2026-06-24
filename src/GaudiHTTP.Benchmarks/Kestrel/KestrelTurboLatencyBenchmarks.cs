using BenchmarkDotNet.Attributes;
using GaudiHTTP.Benchmarks.Internal;
using GaudiHTTP.Client;

namespace GaudiHTTP.Benchmarks.Kestrel;

[MemoryDiagnoser]
[WarmupCount(5)]
[IterationCount(15)]
public class KestrelGaudiLatencyBenchmarks : KestrelBaseClass
{
    private const int Ops = 32;

    private ClientHelper _clientHelper = null!;

    [GlobalSetup]
    public override async Task GlobalSetup()
    {
        await base.GlobalSetup();
        _clientHelper = ClientHelper.CreateClient(BaseAddress, HttpVersionValue);
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
        using var request = new HttpRequestMessage(HttpMethod.Get, LightUri);
        using var response = await _clientHelper.Client.SendAsync(request, CancellationToken.None);
        response.EnsureSuccessStatusCode();
    }

    [Benchmark(OperationsPerInvoke = Ops)]
    public async Task SingleRequestLatency()
    {
        for (var i = 0; i < Ops; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LightUri);
            using var response = await _clientHelper.Client.SendAsync(request, CancellationToken.None);
            response.EnsureSuccessStatusCode();
        }
    }
}
