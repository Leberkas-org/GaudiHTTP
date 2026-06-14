using BenchmarkDotNet.Attributes;
using TurboHTTP.Benchmarks.Internal;
using TurboHTTP.Client;

namespace TurboHTTP.Benchmarks.Kestrel;

/// <summary>
/// Isolated single-request latency for <see cref="ITurboHttpClient.SendAsync"/> against a
/// localhost Kestrel server. Requests are issued strictly sequentially (no fan-out gate), so the
/// mean — divided by <see cref="Ops"/> via <see cref="BenchmarkAttribute.OperationsPerInvoke"/> —
/// is a clean per-request round-trip. This isolates the CL=1 latency gap vs HttpClient that the
/// concurrent suites blur with fan-out and Task.WhenAll machinery.
/// </summary>
[MemoryDiagnoser]
[WarmupCount(5)]
[IterationCount(15)]
public class KestrelTurboLatencyBenchmarks : KestrelBaseClass
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
