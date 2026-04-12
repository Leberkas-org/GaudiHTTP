using BenchmarkDotNet.Attributes;
using TurboHTTP.Benchmarks.Internal;

namespace TurboHTTP.Benchmarks.Binkraken;

/// <summary>
/// Benchmarks measuring <see cref="ITurboHttpClient"/> performance using
/// <see cref="ITurboHttpClient.SendAsync"/> for a single sequential GET request
/// against Binkraken.com over HTTPS.
/// </summary>
[MemoryDiagnoser]
[WarmupCount(3)]
[IterationCount(10)]
public class BinkrakenTurboSendAsyncSingleBenchmarks : BinkrakenBaseClass
{
    private static readonly Uri BaseAddress = new("https://binkraken.com");

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

    /// <summary>GET to <c>https://binkraken.com/</c> (~3 KB response).</summary>
    [Benchmark]
    public async Task SingleRequest_Light()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LightUri);
        using var response = await _clientHelper.Client.SendAsync(request, CancellationToken.None);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>GET to the large JS bundle (~159 KB response).</summary>
    [Benchmark]
    public async Task SingleRequest_Heavy()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, HeavyUri);
        using var response = await _clientHelper.Client.SendAsync(request, CancellationToken.None);
        response.EnsureSuccessStatusCode();
    }
}
