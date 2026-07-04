using BenchmarkDotNet.Attributes;
using GaudiHTTP.Benchmarks.Internal;

namespace GaudiHTTP.Benchmarks.Client.Download;

/// <summary>
/// Baseline large-download throughput for .NET <see cref="HttpClient"/> (SocketsHttpHandler).
/// Mirrors <see cref="GaudiClientDownloadBenchmarks"/> (same sizes, concurrency, fan-out cap, and
/// drain-to-<see cref="Stream.Null"/>) so receive-path throughput is directly comparable.
/// </summary>
[WarmupCount(3)]
[IterationCount(10)]
public class HttpClientDownloadBenchmarks : KestrelBaseClass
{
    [Params(1, 32)]
    public int ConcurrencyLevel { get; set; }

    [Params(1 * 1024 * 1024, 8 * 1024 * 1024)]
    public int DownloadBytes { get; set; }

    private HttpClient _httpClient = null!;
    private Task[] _tasks = null!;
    private SemaphoreSlim _fanOutGate = null!;
    private Uri _downloadUri = null!;

    [GlobalSetup]
    public override async Task GlobalSetup()
    {
        await base.GlobalSetup();
        _httpClient = CreateBaselineHttpClient(timeout: TimeSpan.FromSeconds(120));
        _downloadUri = DownloadUri(DownloadBytes);
        _tasks = new Task[ConcurrencyLevel];
        _fanOutGate = new SemaphoreSlim(MaxInFlight, MaxInFlight);
        await WarmupRequest();
    }

    [GlobalCleanup]
    public override async Task GlobalCleanup()
    {
        _fanOutGate.Dispose();
        _httpClient.Dispose();
        await base.GlobalCleanup();
    }

    /// <inheritdoc />
    public override async Task WarmupRequest()
    {
        await Download();
    }

    [Benchmark]
    public Task ConcurrentDownloads()
    {
        for (var i = 0; i < ConcurrencyLevel; i++)
        {
            _tasks[i] = Download();
        }

        return Task.WhenAll(_tasks).WaitAsync(TimeSpan.FromSeconds(120));
    }

    private async Task Download()
    {
        await _fanOutGate.WaitAsync();
        try
        {
            using var response = await _httpClient.GetAsync(
                _downloadUri, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            await response.Content.CopyToAsync(Stream.Null);
        }
        finally
        {
            _fanOutGate.Release();
        }
    }
}
