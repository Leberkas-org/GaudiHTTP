using System.Net;
using GaudiHTTP.Client;

namespace GaudiHTTP.Benchmarks.Internal;

/// <summary>
/// Standalone client-side allocation trace for the Heavy / HTTP/3 / Streaming scenario.
/// Reproduces the ~1.9 GB allocation figure from the Kestrel report (Heavy, CL=4096, H3) and
/// attributes it by managed type using the in-process <see cref="AllocationProfiler"/>
/// (GCAllocationTick, ~100 KB sampling). Not a BenchmarkDotNet benchmark — invoked via
/// <c>--alloc-trace</c> so it runs in the host process where the client allocates.
/// </summary>
internal static class AllocTraceHarness
{
    public static async Task RunAsync(string version)
    {
        ThreadPool.GetMinThreads(out var w, out var io);
        ThreadPool.SetMinThreads(Math.Max(w, 1024), Math.Max(io, 1024));

        const int requestCount = 4096;
        var payload = KestrelBaseClass.GeneratePayload(1 * 1024 * 1024);

        var (httpVersion, scheme) = version switch
        {
            "1.1" => (HttpVersion.Version11, "http"),
            "2.0" => (HttpVersion.Version20, "http"),
            "3.0" => (HttpVersion.Version30, "https"),
            _ => throw new ArgumentException($"Unknown version '{version}' (expected 1.1, 2.0, or 3.0)"),
        };

        await using var server = new BenchmarkServer();
        await server.InitializeAsync();

        var port = version switch
        {
            "1.1" => server.Http11Port,
            "2.0" => server.Http20Port,
            _ => server.Http30Port,
        };

        if (version == "3.0" && (!server.IsQuicAvailable || server.Http30Port == 0))
        {
            Console.Error.WriteLine("HTTP/3 (QUIC) is not available on this host — cannot run the H3 allocation trace.");
            return;
        }

        var baseAddress = new Uri($"{scheme}://127.0.0.1:{port}");
        var uploadUri = new Uri(baseAddress, "/upload");
        Console.WriteLine($"HTTP/{version} server on {baseAddress}; firing {requestCount} x 1MB uploads through the streaming channel.");

        await using var clientHelper = ClientHelper.CreateStreamingClient(baseAddress, httpVersion);
        var client = clientHelper.Client;

        // Warm up: connection setup, QPACK, JIT — excluded from the armed window.
        await StreamUploads(client, uploadUri, payload, count: 256);

        var profiler = new AllocationProfiler();
        profiler.Reset();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        var before = GC.GetTotalAllocatedBytes(precise: true);
        var (g0, g1, g2) = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));

        profiler.Arm();
        await StreamUploads(client, uploadUri, payload, requestCount);
        profiler.Disarm();

        var after = GC.GetTotalAllocatedBytes(precise: true);
        var totalAllocated = after - before;

        Console.WriteLine();
        Console.WriteLine($"Total managed allocated over {requestCount} uploads: {totalAllocated:N0} B ({totalAllocated / (1024.0 * 1024.0):N1} MB)");
        Console.WriteLine($"Per request: {(double)totalAllocated / requestCount / 1024.0:N1} KB");
        Console.WriteLine($"GC collections during window: gen0={GC.CollectionCount(0) - g0}, gen1={GC.CollectionCount(1) - g1}, gen2={GC.CollectionCount(2) - g2}");
        profiler.Report(requestCount, top: 25);
    }

    private static async Task StreamUploads(IGaudiHttpClient client, Uri uri, byte[] payload, int count)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
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
                var request = new HttpRequestMessage(HttpMethod.Post, uri)
                {
                    Content = new ByteArrayContent(payload),
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
}
