using BenchmarkDotNet.Attributes;
using GaudiHTTP.Benchmarks.Internal;

namespace GaudiHTTP.Benchmarks.Client.Throughput;

/// <summary>
/// Heavy concurrent MB-scale transfers (upload + download) for the GaudiHttp client via SendAsync,
/// at high concurrency with a bounded in-flight window. Targets the zone where GaudiHttp's flow-control
/// already beats HttpClient (H2/H3 heavy concurrent). Body is drained to <see cref="Stream.Null"/>.
/// </summary>
[WarmupCount(3)]
[IterationCount(10)]
public class GaudiClientHeavyConcurrentBenchmarks : KestrelBaseClass
{
    public enum Direction { Upload, Download }

    [Params(Direction.Upload, Direction.Download)]
    public Direction Mode { get; set; }

    [Params(1 * 1024 * 1024, 8 * 1024 * 1024)]
    public int BodySize { get; set; }

    [Params(64, 512)]
    public int ConcurrencyLevel { get; set; }

    private ClientHelper _client = null!;
    private SemaphoreSlim _gate = null!;
    private Task[] _tasks = null!;
    private byte[] _payload = null!;
    private Uri _downloadUri = null!;

    [GlobalSetup]
    public override async Task GlobalSetup()
    {
        await base.GlobalSetup();
        _client = ClientHelper.CreateClient(BaseAddress, HttpVersionValue);
        _payload = GeneratePayload(BodySize);
        _downloadUri = DownloadUri(BodySize);
        _gate = new SemaphoreSlim(MaxInFlight, MaxInFlight);
        _tasks = new Task[ConcurrencyLevel];
        await Transfer();
    }

    [GlobalCleanup]
    public override async Task GlobalCleanup()
    {
        _gate.Dispose();
        await _client.DisposeAsync();
        await base.GlobalCleanup();
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
            using var request = Mode == Direction.Upload
                ? new HttpRequestMessage(HttpMethod.Post, UploadUri) { Content = new ByteArrayContent(_payload) }
                : new HttpRequestMessage(HttpMethod.Get, _downloadUri);
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
