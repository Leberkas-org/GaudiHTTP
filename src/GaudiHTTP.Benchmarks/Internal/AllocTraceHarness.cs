using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using GaudiHTTP.Client;

namespace GaudiHTTP.Benchmarks.Internal;

/// <summary>
/// Client-side allocation trace for the Heavy / Streaming upload scenario. Attributes managed
/// allocation by type using the in-process <see cref="AllocationProfiler"/> (GCAllocationTick,
/// ~100 KB sampling) plus a precise <see cref="GC.GetTotalAllocatedBytes(bool)"/> total. Not a
/// BenchmarkDotNet benchmark.
///
/// Two modes:
/// <list type="bullet">
/// <item><c>--alloc-trace &lt;ver&gt;</c>: server runs IN-PROCESS (legacy). The byte[] total is then
/// contaminated by Kestrel receiving the 1 MB bodies, so only the protocol-DELTA is client-attributable.</item>
/// <item><c>--alloc-trace-client &lt;ver&gt;</c>: the Kestrel server runs in a CHILD process
/// (<c>--bench-server</c>), so every byte measured here is client-side. The full real client path
/// (TCP/TLS/QUIC, framing, transport wrappers) is exercised; only the server's allocations leave the
/// measured process.</item>
/// </list>
/// </summary>
internal static class AllocTraceHarness
{
    private const int RequestCount = 4096;
    private const int WarmupCount = 256;
    private const string PortsPrefix = "PORTS=";

    public static Task RunAsync(string version) => RunAsync(version, clientOnly: false, download: false);

    public static async Task RunAsync(string version, bool clientOnly, bool download = false)
    {
        ThreadPool.GetMinThreads(out var w, out var io);
        ThreadPool.SetMinThreads(Math.Max(w, 1024), Math.Max(io, 1024));

        var (httpVersion, scheme) = version switch
        {
            "1.1" => (HttpVersion.Version11, "http"),
            "2.0" => (HttpVersion.Version20, "http"),
            "3.0" => (HttpVersion.Version30, "https"),
            _ => throw new ArgumentException($"Unknown version '{version}' (expected 1.1, 2.0, or 3.0)"),
        };

        if (clientOnly)
        {
            await RunClientOnlyAsync(version, httpVersion, scheme, download);
        }
        else
        {
            await RunInProcessAsync(version, httpVersion, scheme, download);
        }
    }

    private static async Task RunInProcessAsync(string version, Version httpVersion, string scheme, bool download)
    {
        await using var server = new BenchmarkServer();
        await server.InitializeAsync();

        var port = SelectPort(version, server.Http11Port, server.Http20Port, server.Http30Port);
        if (version == "3.0" && (!server.IsQuicAvailable || port == 0))
        {
            Console.Error.WriteLine("HTTP/3 (QUIC) is not available on this host — cannot run the H3 allocation trace.");
            return;
        }

        var baseAddress = new Uri($"{scheme}://127.0.0.1:{port}");
        Console.WriteLine($"[in-process server] HTTP/{version} on {baseAddress}; server allocations CONTAMINATE the totals.");
        await MeasureClientAsync(baseAddress, httpVersion, version, download);
    }

    private static async Task RunClientOnlyAsync(string version, Version httpVersion, string scheme, bool download)
    {
        using var child = StartServerProcess();

        // Read the port line the child emits once Kestrel is listening.
        int h11 = 0, h20 = 0, h30 = 0;
        var quicAvailable = false;
        string? line;
        while ((line = await child.StandardOutput.ReadLineAsync()) is not null)
        {
            if (line.StartsWith(PortsPrefix, StringComparison.Ordinal))
            {
                var parts = line[PortsPrefix.Length..].Split(',');
                h11 = int.Parse(parts[0]);
                h20 = int.Parse(parts[1]);
                h30 = int.Parse(parts[2]);
                quicAvailable = bool.Parse(parts[3]);
                break;
            }
            Console.WriteLine($"[server] {line}");
        }

        if (line is null)
        {
            Console.Error.WriteLine("Server child process exited before reporting its ports.");
            return;
        }

        try
        {
            var port = SelectPort(version, h11, h20, h30);
            if (version == "3.0" && (!quicAvailable || port == 0))
            {
                Console.Error.WriteLine("HTTP/3 (QUIC) is not available on this host — cannot run the H3 allocation trace.");
                return;
            }

            var baseAddress = new Uri($"{scheme}://127.0.0.1:{port}");
            Console.WriteLine($"[out-of-process server, pid {child.Id}] HTTP/{version} on {baseAddress}; totals are CLIENT-ONLY.");
            await MeasureClientAsync(baseAddress, httpVersion, version, download);
        }
        finally
        {
            try
            {
                child.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already exited.
            }
        }
    }

    /// <summary>
    /// Server-only entry point (<c>--bench-server &lt;ver&gt;</c>). Starts the Kestrel benchmark server,
    /// emits a single <c>PORTS=h11,h20,h30,quic</c> line on stdout, then runs until the parent kills it.
    /// </summary>
    public static async Task RunServerProcessAsync()
    {
        await using var server = new BenchmarkServer();
        await server.InitializeAsync();

        Console.WriteLine($"{PortsPrefix}{server.Http11Port},{server.Http20Port},{server.Http30Port},{server.IsQuicAvailable}");
        await Console.Out.FlushAsync();

        // Stay alive until the parent terminates the process tree.
        await Task.Delay(Timeout.Infinite);
    }

    private static Process StartServerProcess()
    {
        var host = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot determine the host executable path.");
        var entryDll = Assembly.GetEntryAssembly()!.Location;

        var psi = new ProcessStartInfo
        {
            FileName = host,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        // When launched via `dotnet`/`dotnet run`, the host is the dotnet muxer and needs the dll as the
        // first argument; when launched via the apphost exe, the host loads the entry assembly itself.
        if (Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            psi.ArgumentList.Add(entryDll);
        }

        psi.ArgumentList.Add("--bench-server");

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start the benchmark server child process.");
        return process;
    }

    private static int SelectPort(string version, int h11, int h20, int h30) => version switch
    {
        "1.1" => h11,
        "2.0" => h20,
        _ => h30,
    };

    private static async Task MeasureClientAsync(Uri baseAddress, Version httpVersion, string version, bool download)
    {
        var payload = KestrelBaseClass.GeneratePayload(1 * 1024 * 1024);
        var uploadUri = new Uri(baseAddress, "/upload");
        var downloadUri = new Uri(baseAddress, "/download");
        var verb = download ? "downloads" : "uploads";
        Console.WriteLine($"Firing {RequestCount} x 1MB {verb} through the streaming channel.");

        await using var clientHelper = ClientHelper.CreateStreamingClient(baseAddress, httpVersion);
        var client = clientHelper.Client;

        async Task Drive(int count)
        {
            if (download)
            {
                await StreamDownloads(client, downloadUri, count);
            }
            else
            {
                await StreamUploads(client, uploadUri, payload, count);
            }
        }

        // Warm up: connection setup, QPACK, JIT — excluded from the armed window.
        await Drive(WarmupCount);

        var profiler = new AllocationProfiler();
        profiler.Reset();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        var before = GC.GetTotalAllocatedBytes(precise: true);
        var (g0, g1, g2) = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        profiler.Arm();
        await Drive(RequestCount);
        profiler.Disarm();
        sw.Stop();

        var after = GC.GetTotalAllocatedBytes(precise: true);
        var totalAllocated = after - before;
        var rps = RequestCount / sw.Elapsed.TotalSeconds;

        Console.WriteLine();
        Console.WriteLine($"Throughput: {RequestCount} {verb} in {sw.ElapsedMilliseconds:N0} ms = {rps:N0} req/s");
        Console.WriteLine($"Total managed allocated over {RequestCount} {verb}: {totalAllocated:N0} B ({totalAllocated / (1024.0 * 1024.0):N1} MB)");
        Console.WriteLine($"Per request: {(double)totalAllocated / RequestCount / 1024.0:N1} KB");
        Console.WriteLine($"GC collections during window: gen0={GC.CollectionCount(0) - g0}, gen1={GC.CollectionCount(1) - g1}, gen2={GC.CollectionCount(2) - g2}");
        profiler.Report(RequestCount, top: 25);
    }

    private static async Task StreamDownloads(IGaudiHttpClient client, Uri uri, int count)
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
                var request = new HttpRequestMessage(HttpMethod.Get, uri);
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
                // Drain the body into a pooled scratch buffer instead of materializing it with
                // ReadAsByteArrayAsync — a 1 MB array per response would dwarf the production
                // inbound allocation we are trying to isolate.
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
