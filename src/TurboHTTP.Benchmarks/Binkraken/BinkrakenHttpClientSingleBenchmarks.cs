using BenchmarkDotNet.Attributes;
using TurboHTTP.Benchmarks.Internal;

namespace TurboHTTP.Benchmarks.Binkraken;

/// <summary>
/// Baseline benchmarks measuring standard .NET <see cref="HttpClient"/> performance
/// for a single sequential GET request against Binkraken.com over HTTPS.
/// Light (~3 KB HTML) and heavy (~159 KB JS bundle) payloads.
/// </summary>
[MemoryDiagnoser]
[WarmupCount(3)]
[IterationCount(10)]
public class BinkrakenHttpClientSingleBenchmarks : BinkrakenBaseClass
{
    private HttpClient _httpClient = null!;

    [GlobalSetup]
    public override async Task GlobalSetup()
    {
        await base.GlobalSetup();

        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            EnableMultipleHttp2Connections = true,
        };

        _httpClient = new HttpClient(handler)
        {
            DefaultRequestVersion = HttpVersionValue,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };

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

    /// <summary>GET to <c>https://binkraken.com/</c> (~3 KB response).</summary>
    [Benchmark]
    public async Task SingleRequest_Light()
    {
        using var response = await _httpClient.GetAsync(LightUri);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>GET to the large JS bundle (~159 KB response).</summary>
    [Benchmark]
    public async Task SingleRequest_Heavy()
    {
        using var response = await _httpClient.GetAsync(HeavyUri);
        response.EnsureSuccessStatusCode();
    }
}
