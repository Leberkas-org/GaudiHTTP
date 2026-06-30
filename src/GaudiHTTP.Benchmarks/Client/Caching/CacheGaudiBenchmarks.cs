using BenchmarkDotNet.Attributes;
using GaudiHTTP.Benchmarks.Internal;
using GaudiHTTP.Client;

namespace GaudiHTTP.Benchmarks.Client.Caching;

/// <summary>
/// Cache-hit goodput for the GaudiHttp client with <c>WithCache</c> enabled. A <see cref="HitRate"/>
/// fraction of each batch targets one warm, fresh <c>/cacheable</c> URL (served from the client cache,
/// no network); the remainder target unique <c>/uncacheable</c> URLs (network for everyone). The
/// HttpClient baseline (<see cref="CacheHttpClientBenchmarks"/>) has no cache and always hits the network.
/// </summary>
[WarmupCount(3)]
[IterationCount(10)]
public class CacheGaudiBenchmarks : KestrelBaseClass
{
    [Params(50, 90, 100)]
    public int HitRate { get; set; }

    [Params(1 * 1024, 100 * 1024)]
    public int PayloadSize { get; set; }

    [Params(1, 64)]
    public int ConcurrencyLevel { get; set; }

    private ClientHelper _client = null!;
    private Uri _cacheableUri = null!;
    private SemaphoreSlim _gate = null!;
    private Task[] _tasks = null!;

    [GlobalSetup]
    public override async Task GlobalSetup()
    {
        await base.GlobalSetup();
        _client = ClientHelper.CreateFeatureClient(
            BaseAddress, HttpVersionValue, b => b.WithCache(o => { o.MaxEntries = 64; o.MaxBodySize = 1 * 1024 * 1024; }));
        _cacheableUri = CacheableUri(maxAgeSeconds: 300, sizeBytes: PayloadSize);
        _gate = new SemaphoreSlim(MaxInFlight, MaxInFlight);
        _tasks = new Task[ConcurrencyLevel];
        await Fetch(_cacheableUri); // prime the cache
    }

    [GlobalCleanup]
    public override async Task GlobalCleanup()
    {
        _gate.Dispose();
        await _client.DisposeAsync();
        await base.GlobalCleanup();
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
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await _client.Client.SendAsync(request, CancellationToken.None);
            response.EnsureSuccessStatusCode();
            await response.Content.CopyToAsync(Stream.Null);
        }
        finally
        {
            _gate.Release();
        }
    }
}
