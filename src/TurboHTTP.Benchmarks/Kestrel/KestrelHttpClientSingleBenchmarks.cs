using BenchmarkDotNet.Attributes;
using TurboHTTP.Benchmarks.Internal;

namespace TurboHTTP.Benchmarks.Kestrel;

/// <summary>
/// Baseline benchmarks measuring standard .NET <see cref="HttpClient"/> performance
/// for a single sequential request against a localhost Kestrel server.
/// Light (~3 B GET response) and heavy (10 KB POST payload).
/// </summary>
[MemoryDiagnoser]
[WarmupCount(3)]
[IterationCount(10)]
public class KestrelHttpClientSingleBenchmarks : KestrelBaseClass
{
    private HttpClient _httpClient = null!;

    [GlobalSetup]
    public override async Task GlobalSetup()
    {
        await base.GlobalSetup();

        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

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

    /// <summary>GET to <c>/benchmark/simple</c> (~3 B response).</summary>
    [Benchmark]
    public async Task SingleRequest_Light()
    {
        using var response = await _httpClient.GetAsync(LightUri);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>POST to <c>/benchmark/payload</c> with 10 KB body.</summary>
    [Benchmark]
    public async Task SingleRequest_Heavy()
    {
        using var content = new ByteArrayContent(HeavyPayload);
        using var response = await _httpClient.PostAsync(HeavyUri, content);
        response.EnsureSuccessStatusCode();
    }
}
