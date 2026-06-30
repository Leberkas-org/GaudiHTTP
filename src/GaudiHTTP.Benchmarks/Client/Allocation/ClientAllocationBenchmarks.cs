using System.Buffers;
using BenchmarkDotNet.Attributes;
using GaudiHTTP.Benchmarks.Internal;

namespace GaudiHTTP.Benchmarks.Client.Allocation;

/// <summary>
/// Client-only allocation for the Heavy (1 MB) streaming scenario, measured against a server running
/// OUT OF PROCESS (<see cref="ServerProcessHandle"/>) so the process-wide EventPipe allocation
/// (see <see cref="AllocationBenchmarkConfig"/>) reflects only the client — free of in-process Kestrel
/// contamination AND the threadpool throttling it causes. Replaces the <c>--alloc-trace</c> /
/// <c>--alloc-trace-client</c> harness; the per-type + total breakdown comes from
/// <see cref="AllocationByTypeExporter"/>. Standalone (not <c>BenchmarkSuiteBase</c>) to keep the
/// allocation config from merging with the throughput-oriented EngineBenchmarkConfig.
/// </summary>
[Config(typeof(AllocationBenchmarkConfig))]
public class ClientAllocationBenchmarks
{
    public enum Direction
    {
        Upload,
        Download,
    }

    private const int BatchSize = 1024;
    private const int OneMegabyte = 1 * 1024 * 1024;

    private static readonly byte[] Payload = KestrelBaseClass.GeneratePayload(OneMegabyte);

    [Params("1.1", "2.0", "3.0")]
    public string HttpVersion { get; set; } = "1.1";

    [Params(Direction.Upload, Direction.Download)]
    public Direction Mode { get; set; }

    private Version HttpVersionValue => HttpVersion switch
    {
        "3.0" => System.Net.HttpVersion.Version30,
        "2.0" => System.Net.HttpVersion.Version20,
        _ => System.Net.HttpVersion.Version11,
    };

    private ServerProcessHandle _server = null!;
    private ClientHelper _clientHelper = null!;
    private Uri _uri = null!;
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
        _uri = new Uri(baseAddress, Mode == Direction.Upload ? "/upload" : "/download");

        _clientHelper = ClientHelper.CreateStreamingClient(baseAddress, HttpVersionValue);

        await Drive(WarmupBatch);
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        if (_clientHelper is not null)
        {
            await _clientHelper.DisposeAsync();
        }

        _server?.Dispose();
    }

    private const int WarmupBatch = 128;

    [Benchmark]
    public Task StreamHeavy() => _disabled ? Task.CompletedTask : Drive(BatchSize);

    private Task Drive(int count)
        => Mode == Direction.Upload ? StreamUploads(count) : StreamDownloads(count);

    private async Task StreamUploads(int count)
    {
        var client = _clientHelper.Client;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = cts.Token;

        while (client.Responses.TryRead(out var stale))
        {
            stale.Dispose();
        }

        using var throttle = new SemaphoreSlim(Math.Min(count, 512));

        var writer = Task.Run(async () =>
        {
            for (var i = 0; i < count; i++)
            {
                await throttle.WaitAsync(ct);
                var request = new HttpRequestMessage(HttpMethod.Post, _uri)
                {
                    Content = new ByteArrayContent(Payload),
                };
                await client.Requests.WriteAsync(request, ct);
            }
        }, ct);

        var received = 0;
        while (received < count)
        {
            if (!await client.Responses.WaitToReadAsync(ct))
            {
                break;
            }

            while (client.Responses.TryRead(out var response))
            {
                await response.Content.ReadAsByteArrayAsync(ct);
                response.Dispose();
                throttle.Release();
                if (++received >= count)
                {
                    break;
                }
            }
        }

        await writer.WaitAsync(ct);
    }

    private async Task StreamDownloads(int count)
    {
        var client = _clientHelper.Client;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = cts.Token;

        while (client.Responses.TryRead(out var stale))
        {
            stale.Dispose();
        }

        using var throttle = new SemaphoreSlim(Math.Min(count, 512));

        var writer = Task.Run(async () =>
        {
            for (var i = 0; i < count; i++)
            {
                await throttle.WaitAsync(ct);
                var request = new HttpRequestMessage(HttpMethod.Get, _uri);
                await client.Requests.WriteAsync(request, ct);
            }
        }, ct);

        var received = 0;
        while (received < count)
        {
            if (!await client.Responses.WaitToReadAsync(ct))
            {
                break;
            }

            while (client.Responses.TryRead(out var response))
            {
                // Drain into a pooled scratch buffer rather than materializing a 1 MB array per
                // response, which would dwarf the client allocation under measurement.
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

                response.Dispose();
                throttle.Release();
                if (++received >= count)
                {
                    break;
                }
            }
        }

        await writer.WaitAsync(ct);
    }
}
