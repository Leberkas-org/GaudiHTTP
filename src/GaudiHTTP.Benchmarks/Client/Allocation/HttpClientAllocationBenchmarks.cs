using System.Buffers;
using BenchmarkDotNet.Attributes;
using GaudiHTTP.Benchmarks.Internal;

namespace GaudiHTTP.Benchmarks.Client.Allocation;

/// <summary>
/// HttpClient baseline for <see cref="ClientAllocationBenchmarks"/>: the same out-of-process,
/// single-client allocation measurement (process-wide EventPipe total via
/// <see cref="AllocationBenchmarkConfig"/>) for upload/download at MB scale, using SocketsHttpHandler.
/// Compared offline against the GaudiHttp numbers — never in-process (cross-client allocation is not
/// attributable in one process).
/// </summary>
[Config(typeof(AllocationBenchmarkConfig))]
public class HttpClientAllocationBenchmarks
{
    public enum Direction
    {
        Upload,
        Download,
    }

    private const int BatchSize = 1024;
    private const int WarmupBatch = 128;

    [Params("1.1", "2.0", "3.0")]
    public string HttpVersion { get; set; } = "1.1";

    [Params(Direction.Upload, Direction.Download)]
    public Direction Mode { get; set; }

    [Params(1 * 1024 * 1024, 8 * 1024 * 1024)]
    public int BodySize { get; set; }

    private Version HttpVersionValue => HttpVersion switch
    {
        "3.0" => System.Net.HttpVersion.Version30,
        "2.0" => System.Net.HttpVersion.Version20,
        _ => System.Net.HttpVersion.Version11,
    };

    private ServerProcessHandle _server = null!;
    private HttpClient _client = null!;
    private byte[] _payload = null!;
    private Uri _uploadUri = null!;
    private Uri _downloadUri = null!;
    private bool _disabled;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        ThreadPool.GetMinThreads(out var w, out var io);
        ThreadPool.SetMinThreads(Math.Max(w, 1024), Math.Max(io, 1024));

        _server = await ServerProcessHandle.StartAsync();
        if (HttpVersion == "3.0" && !_server.QuicAvailable)
        {
            _disabled = true;
            return;
        }

        var scheme = HttpVersion == "3.0" ? "https" : "http";
        var baseAddress = new Uri($"{scheme}://127.0.0.1:{_server.PortFor(HttpVersion)}");
        _uploadUri = new Uri(baseAddress, "/upload");
        _downloadUri = new Uri(baseAddress, $"/download?size={BodySize}");
        _payload = KestrelBaseClass.GeneratePayload(BodySize);

        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        var handler = new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,
            MaxConnectionsPerServer = 256,
            SslOptions = { RemoteCertificateValidationCallback = (_, _, _, _) => true },
        };
        _client = new HttpClient(handler)
        {
            DefaultRequestVersion = HttpVersionValue,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Timeout = TimeSpan.FromMinutes(3),
        };

        await Drive(WarmupBatch);
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _client?.Dispose();
        _server?.Dispose();
    }

    [Benchmark]
    public Task TransferHeavy() => _disabled ? Task.CompletedTask : Drive(BatchSize);

    private async Task Drive(int count)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var ct = cts.Token;
        using var throttle = new SemaphoreSlim(Math.Min(count, 256));
        var tasks = new Task[count];
        for (var i = 0; i < count; i++)
        {
            tasks[i] = One(throttle, ct);
        }

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(120), ct);
    }

    private async Task One(SemaphoreSlim throttle, CancellationToken ct)
    {
        await throttle.WaitAsync(ct);
        try
        {
            // Set BOTH Version and VersionPolicy: HttpClient.SendAsync does NOT apply
            // DefaultRequestVersion/DefaultVersionPolicy to an explicit HttpRequestMessage (only
            // GetAsync/PostAsync do); without RequestVersionExact the request downgrades to HTTP/1.1
            // on the H2/H3-only ports -> 400.
            using var request = Mode == Direction.Upload
                ? new HttpRequestMessage(HttpMethod.Post, _uploadUri)
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
            using var response = await _client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            if (Mode == Direction.Download)
            {
                var scratch = ArrayPool<byte>.Shared.Rent(64 * 1024);
                try
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(ct);
                    while (await stream.ReadAsync(scratch, ct) > 0)
                    {
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(scratch);
                }
            }
            else
            {
                await response.Content.ReadAsByteArrayAsync(ct);
            }
        }
        finally
        {
            throttle.Release();
        }
    }
}
