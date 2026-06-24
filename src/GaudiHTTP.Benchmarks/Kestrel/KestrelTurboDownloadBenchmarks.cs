using BenchmarkDotNet.Attributes;
using GaudiHTTP.Benchmarks.Internal;
using GaudiHTTP.Client;

namespace GaudiHTTP.Benchmarks.Kestrel;

/// <summary>
/// Large-download throughput for <see cref="IGaudiHttpClient.SendAsync"/> against a localhost
/// Kestrel server. Exercises the response receive path (framing decode → QueuedBodyReader →
/// transport reads) that the upload/light suites never touch. Each request fully drains the
/// response body into <see cref="Stream.Null"/> so only receive-path cost is measured.
/// </summary>
[MemoryDiagnoser]
[WarmupCount(3)]
[IterationCount(10)]
public class KestrelGaudiDownloadBenchmarks : KestrelBaseClass
{
    [Params(1, 32)]
    public int ConcurrencyLevel { get; set; }

    [Params(1 * 1024 * 1024, 8 * 1024 * 1024)]
    public int DownloadBytes { get; set; }

    private ClientHelper _clientHelper = null!;
    private Task[] _tasks = null!;
    private SemaphoreSlim _fanOutGate = null!;
    private Uri _downloadUri = null!;

    [GlobalSetup]
    public override async Task GlobalSetup()
    {
        await base.GlobalSetup();
        _clientHelper = ClientHelper.CreateClient(BaseAddress, HttpVersionValue);
        _downloadUri = DownloadUri(DownloadBytes);
        _tasks = new Task[ConcurrencyLevel];
        _fanOutGate = new SemaphoreSlim(MaxInFlight, MaxInFlight);
        await WarmupRequest();
    }

    [GlobalCleanup]
    public override async Task GlobalCleanup()
    {
        _fanOutGate.Dispose();
        await _clientHelper.DisposeAsync();
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
            using var request = new HttpRequestMessage(HttpMethod.Get, _downloadUri);
            using var response = await _clientHelper.Client.SendAsync(request, CancellationToken.None);
            response.EnsureSuccessStatusCode();
            await response.Content.CopyToAsync(Stream.Null);
        }
        finally
        {
            _fanOutGate.Release();
        }
    }
}
