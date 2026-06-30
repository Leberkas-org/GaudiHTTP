using BenchmarkDotNet.Attributes;
using GaudiHTTP.Benchmarks.Internal;

namespace GaudiHTTP.Benchmarks.Client.Throughput;

/// <summary>
/// HttpClient baseline for <see cref="HeavyConcurrentGaudiBenchmarks"/>: identical heavy concurrent
/// upload/download matrix via SocketsHttpHandler + SendAsync.
/// </summary>
[WarmupCount(3)]
[IterationCount(10)]
public class HeavyConcurrentHttpClientBenchmarks : KestrelBaseClass
{
    public enum Direction { Upload, Download }

    [Params(Direction.Upload, Direction.Download)]
    public Direction Mode { get; set; }

    [Params(1 * 1024 * 1024, 8 * 1024 * 1024)]
    public int BodySize { get; set; }

    [Params(64, 512)]
    public int ConcurrencyLevel { get; set; }

    private HttpClient _client = null!;
    private SemaphoreSlim _gate = null!;
    private Task[] _tasks = null!;
    private byte[] _payload = null!;
    private Uri _downloadUri = null!;

    [GlobalSetup]
    public override async Task GlobalSetup()
    {
        await base.GlobalSetup();
        _client = CreateBaselineHttpClient(maxConnectionsPerServer: MaxInFlight, timeout: TimeSpan.FromMinutes(3));
        _payload = GeneratePayload(BodySize);
        _downloadUri = DownloadUri(BodySize);
        _gate = new SemaphoreSlim(MaxInFlight, MaxInFlight);
        _tasks = new Task[ConcurrencyLevel];
        await Transfer();
    }

    [GlobalCleanup]
    public override Task GlobalCleanup()
    {
        _gate.Dispose();
        _client.Dispose();
        return base.GlobalCleanup();
    }

    [Benchmark]
    public Task ConcurrentTransfers()
    {
        for (var i = 0; i < ConcurrencyLevel; i++)
        {
            _tasks[i] = Transfer();
        }

        return Task.WhenAll(_tasks).WaitAsync(TimeSpan.FromSeconds(180));
    }

    private async Task Transfer()
    {
        await _gate.WaitAsync();
        try
        {
            // Set BOTH Version and VersionPolicy: HttpClient.SendAsync does NOT apply the client's
            // DefaultRequestVersion/DefaultVersionPolicy to an explicit HttpRequestMessage (only
            // GetAsync/PostAsync do). Without VersionPolicy=RequestVersionExact the request downgrades to
            // HTTP/1.1 on the H2/H3-only Kestrel ports -> 400.
            using var request = Mode == Direction.Upload
                ? new HttpRequestMessage(HttpMethod.Post, UploadUri)
                {
                    Content = new ByteArrayContent(_payload),
                    Version = HttpVersionValue,
                    VersionPolicy = HttpVersionPolicy.RequestVersionExact,
                }
                : new HttpRequestMessage(HttpMethod.Get, _downloadUri)
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
