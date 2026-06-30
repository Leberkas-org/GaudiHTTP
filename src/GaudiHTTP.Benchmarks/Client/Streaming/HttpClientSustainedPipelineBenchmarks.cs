using BenchmarkDotNet.Attributes;
using GaudiHTTP.Benchmarks.Internal;

namespace GaudiHTTP.Benchmarks.Client.Streaming;

/// <summary>
/// HttpClient baseline for <see cref="GaudiClientSustainedPipelineBenchmarks"/>: a sustained pipeline bounded
/// by a <see cref="SemaphoreSlim"/> in-flight window (the manual equivalent of the channel API).
/// </summary>
[WarmupCount(3)]
[IterationCount(10)]
public class HttpClientSustainedPipelineBenchmarks : KestrelBaseClass
{
    public enum Workload { Light, Slow }

    private const int TotalRequests = 2000;

    [Params(64, 256, 1024)]
    public int InFlightWindow { get; set; }

    [Params(Workload.Light, Workload.Slow)]
    public Workload Mode { get; set; }

    private HttpClient _client = null!;
    private Uri _uri = null!;

    [GlobalSetup]
    public override async Task GlobalSetup()
    {
        await base.GlobalSetup();
        _client = CreateBaselineHttpClient(maxConnectionsPerServer: 1024, timeout: TimeSpan.FromMinutes(2));
        _uri = Mode == Workload.Slow ? SlowUri(2) : LightUri;
        await WarmupRequest();
    }

    [GlobalCleanup]
    public override Task GlobalCleanup()
    {
        _client.Dispose();
        return base.GlobalCleanup();
    }

    public override async Task WarmupRequest()
    {
        // Set BOTH Version and VersionPolicy on every explicit HttpRequestMessage: HttpClient.SendAsync
        // does NOT apply DefaultRequestVersion/DefaultVersionPolicy to a request you construct yourself
        // (only GetAsync/PostAsync do). Without VersionPolicy=RequestVersionExact the request downgrades
        // to HTTP/1.1 on the H2/H3-only Kestrel ports -> 400.
        using var request = new HttpRequestMessage(HttpMethod.Get, _uri)
        {
            Version = HttpVersionValue,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
        using var response = await _client.SendAsync(request, CancellationToken.None);
        response.EnsureSuccessStatusCode();
    }

    [Benchmark(OperationsPerInvoke = TotalRequests)]
    public async Task SustainedPipeline()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var ct = cts.Token;
        using var window = new SemaphoreSlim(InFlightWindow, InFlightWindow);

        var tasks = new Task[TotalRequests];
        for (var i = 0; i < TotalRequests; i++)
        {
            tasks[i] = One(window, ct);
        }

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(120), ct);
    }

    private async Task One(SemaphoreSlim window, CancellationToken ct)
    {
        await window.WaitAsync(ct);
        try
        {
            // Set both Version and VersionPolicy (see WarmupRequest note).
            using var request = new HttpRequestMessage(HttpMethod.Get, _uri)
            {
                Version = HttpVersionValue,
                VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            };
            using var response = await _client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            await response.Content.CopyToAsync(Stream.Null, ct);
        }
        finally
        {
            window.Release();
        }
    }
}
