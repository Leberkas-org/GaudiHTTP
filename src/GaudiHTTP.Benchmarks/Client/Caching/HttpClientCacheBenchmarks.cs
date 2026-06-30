using BenchmarkDotNet.Attributes;
using GaudiHTTP.Benchmarks.Internal;

namespace GaudiHTTP.Benchmarks.Client.Caching;

/// <summary>
/// HttpClient baseline for <see cref="GaudiClientCacheBenchmarks"/>: no response cache, so every request —
/// hit fraction included — is a real network round-trip. Identical params for apples-to-apples.
/// </summary>
[WarmupCount(3)]
[IterationCount(10)]
public class HttpClientCacheBenchmarks : KestrelBaseClass
{
    [Params(50, 90, 100)]
    public int HitRate { get; set; }

    [Params(1 * 1024, 100 * 1024)]
    public int PayloadSize { get; set; }

    [Params(1, 64)]
    public int ConcurrencyLevel { get; set; }

    private HttpClient _client = null!;
    private Uri _cacheableUri = null!;
    private SemaphoreSlim _gate = null!;
    private Task[] _tasks = null!;

    [GlobalSetup]
    public override async Task GlobalSetup()
    {
        await base.GlobalSetup();
        _client = CreateBaselineHttpClient(maxConnectionsPerServer: MaxInFlight, timeout: TimeSpan.FromMinutes(2));
        _cacheableUri = CacheableUri(maxAgeSeconds: 300, sizeBytes: PayloadSize);
        _gate = new SemaphoreSlim(MaxInFlight, MaxInFlight);
        _tasks = new Task[ConcurrencyLevel];
        await Fetch(_cacheableUri);
    }

    [GlobalCleanup]
    public override Task GlobalCleanup()
    {
        _gate.Dispose();
        _client.Dispose();
        return base.GlobalCleanup();
    }

    [Benchmark]
    public Task CacheMixedWorkload()
    {
        var hits = ConcurrencyLevel * HitRate / 100;
        for (var i = 0; i < ConcurrencyLevel; i++)
        {
            _tasks[i] = i < hits
                ? Fetch(_cacheableUri)
                : Fetch(UncacheableUri(Guid.NewGuid().ToString("N"), PayloadSize));
        }

        return Task.WhenAll(_tasks).WaitAsync(TimeSpan.FromSeconds(60));
    }

    private async Task Fetch(Uri uri)
    {
        await _gate.WaitAsync();
        try
        {
            // Set both Version and VersionPolicy per-request: HttpClient.PrepareRequestMessage does NOT
            // propagate DefaultRequestVersion/DefaultVersionPolicy to explicit HttpRequestMessage instances —
            // only the GetAsync/PostAsync convenience methods do (via the private CreateRequestMessage helper).
            // Without VersionPolicy = RequestVersionExact the default RequestVersionOrLower causes
            // SocketsHttpHandler to downgrade to HTTP/1.1 on the h2c-only Kestrel port -> 400.
            using var request = new HttpRequestMessage(HttpMethod.Get, uri)
            {
                Version = HttpVersionValue,
                VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            };
            using var response = await _client.SendAsync(request, CancellationToken.None);
            response.EnsureSuccessStatusCode();
            await response.Content.CopyToAsync(Stream.Null);
        }
        finally
        {
            _gate.Release();
        }
    }
}
