using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;

namespace TurboHTTP.Benchmarks.LoadTest;

// Open-loop, raw-socket load driver. BenchmarkDotNet is closed-loop (fixed-batch latency-to-drain)
// and HttpClient does not pipeline HTTP/1.1, so neither can exercise the transport send path under
// sustained load. This driver opens N persistent keep-alive sockets, pipelines D plaintext requests
// per round, and pushes for a fixed duration to measure achieved RPS + round-trip latency.
//
// It targets exactly the two transport changes it exists to measure:
//   - pipeline depth > 1 accumulates multiple responses in the output pipe -> vectored writev,
//   - high connection count -> batched IOQueue scheduler.
//
// Each server runs in its OWN child process (spawned as `loadtest --serve <kind>`). The driver reads
// the child's SERVER-ONLY GC counters via the /__allocstats endpoint before and after the measured
// window, so the reported alloc/req excludes all client/generator allocations — unlike the old
// in-process design where GC.GetTotalAllocatedBytes mixed both heaps.
internal static class OpenLoopLoadTest
{
    public static async Task RunAsync(LoadTestOptions options)
    {
        Console.WriteLine($"Open-loop load test | protocol={options.Protocol} duration={options.DurationSeconds}s warmup={options.WarmupSeconds}s "
            + $"connections={options.Connections} pipeline={options.PipelineDepth} route={options.Route}");
        Console.WriteLine("Mode: out-of-process server measurement (server alloc/req excludes the client).");
        if (options.Profile)
        {
            Console.WriteLine("Note: --profile is ignored in out-of-process mode (it only measures the in-process heap).");
        }

        Console.WriteLine(new string('-', 88));

        var results = new List<LoadResult>();

        if (options.RunTurbo)
        {
            results.Add(await MeasureAsync("TurboServer", "turbo", options));
        }

        if (options.RunKestrel)
        {
            results.Add(await MeasureAsync("Kestrel", "kestrel", options));
        }

        PrintComparison(results);
    }

    private static async Task<LoadResult> MeasureAsync(string name, string kind, LoadTestOptions options)
    {
        var child = StartServerProcess(kind);
        try
        {
            var ports = await ReadPortsAsync(child);
            var port = options.Protocol switch
            {
                "h2" => ports.H2,
                "h3" => ports.H3,
                _ => ports.H1,
            };
            return await DriveAsync(name, port, ports.H1, options);
        }
        finally
        {
            KillChild(child);
        }
    }

    private static Process StartServerProcess(string kind)
    {
        // Robust under `dotnet run`: invoke `dotnet exec <benchmarks.dll> loadtest --serve <kind>`.
        var dll = Assembly.GetExecutingAssembly().Location;
        var dotnet = Environment.ProcessPath ?? "dotnet";

        var psi = new ProcessStartInfo
        {
            FileName = dotnet,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        // If the host is the dotnet muxer, run via `exec <dll>`; otherwise the host IS the apphost exe.
        if (Path.GetFileNameWithoutExtension(dotnet).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            psi.ArgumentList.Add("exec");
            psi.ArgumentList.Add(dll);
        }

        psi.ArgumentList.Add("loadtest");
        psi.ArgumentList.Add("--serve");
        psi.ArgumentList.Add(kind);

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start child server process for '{kind}'.");

        return process;
    }

    private static async Task<(int H1, int H2, int H3)> ReadPortsAsync(Process child)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        while (!timeout.IsCancellationRequested)
        {
            var line = await child.StandardOutput.ReadLineAsync(timeout.Token);
            if (line is null)
            {
                throw new InvalidOperationException("Child server exited before reporting its port.");
            }

            if (line.StartsWith("PORT=", StringComparison.Ordinal))
            {
                var parts = line["PORT=".Length..].Split(';');
                var h1 = int.Parse(parts[0]);
                var h2 = parts.Length > 1 && parts[1].StartsWith("H2=", StringComparison.Ordinal)
                    ? int.Parse(parts[1]["H2=".Length..])
                    : h1;
                var h3 = parts.Length > 2 && parts[2].StartsWith("H3=", StringComparison.Ordinal)
                    ? int.Parse(parts[2]["H3=".Length..])
                    : h1;
                return (h1, h2, h3);
            }
        }

        throw new TimeoutException("Timed out waiting for child server to report its port.");
    }

    private static void KillChild(Process child)
    {
        try
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                child.WaitForExit(TimeSpan.FromSeconds(10));
            }
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
        finally
        {
            child.Dispose();
        }
    }

    private static async Task<(long Alloc, int Gen0, int Gen1, int Gen2)> FetchAllocStatsAsync(int port)
    {
        // Driver-side HttpClient; its allocations land on the DRIVER heap, never the child server's.
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        var text = await http.GetStringAsync("/__allocstats");
        var parts = text.Split(';');
        return (long.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]), int.Parse(parts[3]));
    }

    private static async Task ResetAllocTypesAsync(int port)
    {
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        await http.GetStringAsync("/__allocreset");
    }

    private static async Task<List<(long Hits, long Bytes, string Type)>> FetchAllocTypesAsync(int port)
    {
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        var text = await http.GetStringAsync("/__alloctypes");

        var rows = new List<(long, long, string)>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length == 3
                && long.TryParse(parts[0], out var hits)
                && long.TryParse(parts[1], out var bytes))
            {
                rows.Add((hits, bytes, parts[2]));
            }
        }

        return rows;
    }

    private static void PrintAllocTypes(string name, List<(long Hits, long Bytes, string Type)> rows, long requests)
    {
        Console.WriteLine();
        Console.WriteLine($"{name} server-only allocations by type (top {Math.Min(20, rows.Count)}, "
            + "GCAllocationTick sample — ranked by ~B/req; Hits shown for context):");
        if (rows.Count == 0)
        {
            Console.WriteLine("  (no GCAllocationTick samples captured)");
            return;
        }

        Console.WriteLine($"{"Type",-62}{"~B/req",12}{"Hits",10}");
        Console.WriteLine(new string('-', 84));
        foreach (var (hits, bytes, type) in rows.Take(20))
        {
            var shortType = type.Length > 60 ? "…" + type[^59..] : type;
            var perReq = requests == 0 ? 0 : (double)bytes / requests;
            Console.WriteLine($"{shortType,-62}{perReq,12:N1}{hits,10:N0}");
        }
    }

    private static async Task<LoadResult> DriveAsync(string name, int port, int statsPort, LoadTestOptions options)
    {
        Func<CancellationToken, Task<(long, List<double>)>> runPhase;

        if (options.Protocol is "h1-client" or "h2" or "h3")
        {
            var version = options.Protocol switch
            {
                "h3" => HttpVersion.Version30,
                "h2" => HttpVersion.Version20,
                _ => HttpVersion.Version11,
            };
            var scheme = options.Protocol == "h3" ? "https" : "http";
            var baseUrl = string.Concat(scheme, "://127.0.0.1:", port.ToString());
            runPhase = ct => RunHttpClientPhaseAsync(
                baseUrl, version, options.Connections, options.PipelineDepth,
                options.Route, ct);
        }
        else
        {
            var endpoint = new IPEndPoint(IPAddress.Loopback, port);
            var requestBlob = BuildPipelinedRequest(options.Route, options.PipelineDepth);
            var responseLength = await ProbeResponseLengthAsync(endpoint, options.Route);
            var batchBytes = responseLength * options.PipelineDepth;
            runPhase = ct => RunPhaseAsync(endpoint, requestBlob, batchBytes, options, collectLatencies: true, ct);
        }

        if (options.WarmupSeconds > 0)
        {
            using var warmupCts = new CancellationTokenSource(TimeSpan.FromSeconds(options.WarmupSeconds));
            await runPhase(warmupCts.Token);
        }

        var gcBefore = await FetchAllocStatsAsync(statsPort);
        await ResetAllocTypesAsync(statsPort);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(options.DurationSeconds));
        var sw = Stopwatch.StartNew();
        var (requests, latencies) = await runPhase(cts.Token);
        sw.Stop();

        var gcAfter = await FetchAllocStatsAsync(statsPort);
        var allocDelta = gcAfter.Alloc - gcBefore.Alloc;

        // Server-only per-type breakdown for the measured window (ranked by HITS in the child).
        var allocTypes = await FetchAllocTypesAsync(statsPort);
        PrintAllocTypes(name, allocTypes, requests);

        latencies.Sort();
        return new LoadResult(
            name,
            requests,
            sw.Elapsed.TotalSeconds,
            requests / sw.Elapsed.TotalSeconds,
            Percentile(latencies, 0.50),
            Percentile(latencies, 0.99),
            requests == 0 ? 0 : (double)allocDelta / requests,
            gcAfter.Gen0 - gcBefore.Gen0,
            gcAfter.Gen1 - gcBefore.Gen1,
            gcAfter.Gen2 - gcBefore.Gen2);
    }

    private static async Task<(long Requests, List<double> Latencies)> RunPhaseAsync(
        IPEndPoint endpoint,
        byte[] requestBlob,
        int batchBytes,
        LoadTestOptions options,
        bool collectLatencies,
        CancellationToken ct)
    {
        var workers = new Task<(long, List<double>)>[options.Connections];
        for (var i = 0; i < options.Connections; i++)
        {
            workers[i] = RunConnectionAsync(endpoint, requestBlob, batchBytes, options.PipelineDepth, collectLatencies, ct);
        }

        var completed = await Task.WhenAll(workers);

        long total = 0;
        var latencies = new List<double>();
        foreach (var (count, lat) in completed)
        {
            total += count;
            if (collectLatencies)
            {
                latencies.AddRange(lat);
            }
        }

        return (total, latencies);
    }

    private static async Task<(long Requests, List<double> Latencies)> RunHttpClientPhaseAsync(
        string baseUrl,
        Version httpVersion,
        int connections,
        int concurrencyPerConnection,
        string route,
        CancellationToken ct)
    {
        long totalRequests = 0;
        var allLatencies = new List<double>();
        var workers = new Task[connections * concurrencyPerConnection];
        var perWorkerLatencies = new List<double>[workers.Length];
        var handlers = new List<SocketsHttpHandler>(connections);

        for (var c = 0; c < connections; c++)
        {
            var handler = new SocketsHttpHandler
            {
                MaxConnectionsPerServer = 1,
                EnableMultipleHttp2Connections = false,
                SslOptions =
                {
                    RemoteCertificateValidationCallback = (_, _, _, _) => true,
                },
            };
            handlers.Add(handler);

            var client = new HttpClient(handler, disposeHandler: false)
            {
                BaseAddress = new Uri(baseUrl),
                DefaultRequestVersion = httpVersion,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
            };

            for (var s = 0; s < concurrencyPerConnection; s++)
            {
                var idx = c * concurrencyPerConnection + s;
                var lats = new List<double>(4096);
                perWorkerLatencies[idx] = lats;
                workers[idx] = Task.Run(async () =>
                {
                    try
                    {
                        while (!ct.IsCancellationRequested)
                        {
                            var start = Stopwatch.GetTimestamp();
                            using var response = await client.GetAsync(route, ct);
                            await response.Content.ReadAsByteArrayAsync(ct);
                            Interlocked.Increment(ref totalRequests);
                            lats.Add(Stopwatch.GetElapsedTime(start).TotalMicroseconds);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (HttpRequestException)
                    {
                    }
                });
            }
        }

        await Task.WhenAll(workers);

        foreach (var lats in perWorkerLatencies)
        {
            if (lats is not null)
            {
                allLatencies.AddRange(lats);
            }
        }

        return (totalRequests, allLatencies);
    }

    private static async Task<(long, List<double>)> RunConnectionAsync(
        IPEndPoint endpoint,
        byte[] requestBlob,
        int batchBytes,
        int pipelineDepth,
        bool collectLatencies,
        CancellationToken ct)
    {
        long requests = 0;
        var latencies = collectLatencies ? new List<double>(capacity: 4096) : null;
        var readBuffer = new byte[Math.Max(batchBytes, 4096)];

        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true,
        };

        try
        {
            await socket.ConnectAsync(endpoint, ct);

            while (!ct.IsCancellationRequested)
            {
                var start = collectLatencies ? Stopwatch.GetTimestamp() : 0L;

                await SendAllAsync(socket, requestBlob, ct);

                var read = 0;
                while (read < batchBytes)
                {
                    var n = await socket.ReceiveAsync(readBuffer.AsMemory(read, batchBytes - read), ct);
                    if (n == 0)
                    {
                        return (requests, latencies ?? []);
                    }

                    read += n;
                }

                requests += pipelineDepth;
                if (collectLatencies)
                {
                    // Round-trip latency for the whole pipelined round, in microseconds.
                    latencies!.Add(Stopwatch.GetElapsedTime(start).TotalMicroseconds);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Deadline reached mid-round; counted requests stand.
        }
        catch (Exception ex) when (ex is SocketException or IOException or ObjectDisposedException)
        {
            // Connection torn down during shutdown; counted requests stand.
        }

        return (requests, latencies ?? []);
    }

    private static async Task SendAllAsync(Socket socket, byte[] buffer, CancellationToken ct)
    {
        var sent = 0;
        while (sent < buffer.Length)
        {
            var n = await socket.SendAsync(buffer.AsMemory(sent), SocketFlags.None, ct);
            sent += n;
        }
    }

    // Measures the exact byte length of a single response. The plaintext/json/fortunes responses are
    // fixed-shape (the Date header value changes but its length is constant), so one probe yields a
    // length valid for the whole run, letting workers count completed responses by byte total.
    private static async Task<int> ProbeResponseLengthAsync(IPEndPoint endpoint, string route)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true,
        };
        await socket.ConnectAsync(endpoint);
        await socket.SendAsync(BuildPipelinedRequest(route, 1), SocketFlags.None);

        var buffer = new byte[16 * 1024];
        var read = 0;
        var contentLength = -1;
        var headerEnd = -1;

        while (true)
        {
            var n = await socket.ReceiveAsync(buffer.AsMemory(read), SocketFlags.None);
            if (n == 0)
            {
                throw new InvalidOperationException($"Server closed connection during probe of {route}.");
            }

            read += n;
            var span = buffer.AsSpan(0, read);

            if (headerEnd < 0)
            {
                var idx = span.IndexOf("\r\n\r\n"u8);
                if (idx >= 0)
                {
                    headerEnd = idx + 4;
                    contentLength = ParseContentLength(span[..idx]);
                }
            }

            if (headerEnd >= 0 && read >= headerEnd + contentLength)
            {
                return headerEnd + contentLength;
            }
        }
    }

    private static int ParseContentLength(ReadOnlySpan<byte> headers)
    {
        var marker = "Content-Length:"u8;
        var idx = headers.IndexOf(marker);
        if (idx < 0)
        {
            throw new InvalidOperationException("Probe response has no Content-Length header.");
        }

        var rest = headers[(idx + marker.Length)..];
        var lineEnd = rest.IndexOf((byte)'\r');
        var value = (lineEnd >= 0 ? rest[..lineEnd] : rest).Trim((byte)' ');
        return int.Parse(Encoding.ASCII.GetString(value));
    }

    private static byte[] BuildPipelinedRequest(string route, int depth)
    {
        var single = $"GET {route} HTTP/1.1\r\nHost: localhost\r\n\r\n";
        var sb = new StringBuilder(single.Length * depth);
        for (var i = 0; i < depth; i++)
        {
            sb.Append(single);
        }

        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0)
        {
            return 0;
        }

        var rank = (int)Math.Ceiling(p * sorted.Count) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Count - 1)];
    }

    private static void PrintComparison(List<LoadResult> results)
    {
        Console.WriteLine();
        Console.WriteLine($"{"Server",-14}{"Requests",12}{"RPS",14}{"P50 us",12}{"P99 us",12}{"Srv B/req",14}{"GC 0/1/2",14}");
        Console.WriteLine(new string('-', 92));
        foreach (var r in results)
        {
            Console.WriteLine(
                $"{r.Name,-14}{r.Requests,12:N0}{r.RequestsPerSecond,14:N0}{r.P50Micros,12:N1}{r.P99Micros,12:N1}"
                + $"{r.AllocBytesPerRequest,14:N1}{$"{r.Gen0}/{r.Gen1}/{r.Gen2}",14}");
        }

        var turbo = results.FirstOrDefault(r => r.Name == "TurboServer");
        var kestrel = results.FirstOrDefault(r => r.Name == "Kestrel");

        if (turbo.Name is not null && kestrel.Name is not null)
        {
            var ratio = turbo.RequestsPerSecond / kestrel.RequestsPerSecond;
            var allocDiff = turbo.AllocBytesPerRequest - kestrel.AllocBytesPerRequest;
            var allocPct = kestrel.AllocBytesPerRequest == 0
                ? 0
                : allocDiff / kestrel.AllocBytesPerRequest;

            Console.WriteLine();
            Console.WriteLine($"TurboServer RPS / Kestrel RPS = {ratio:P1}");
            Console.WriteLine(
                $"Server alloc/req diff (Turbo - Kestrel) = {allocDiff:N1} B ({allocPct:+0.0%;-0.0%;0.0%})");
        }

        Console.WriteLine();
        Console.WriteLine("Note: 'Srv B/req' is the SERVER process's own alloc/req (measured out-of-process; "
            + "excludes the client).");
    }
}
