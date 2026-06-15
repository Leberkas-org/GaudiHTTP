using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TurboHTTP.Benchmarks.Internal;

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
// Caveat: client and server share this process (loopback), so the generator competes for cores with
// the server. Treat the numbers as relative Turbo-vs-Kestrel comparisons, not absolute throughput.
internal static class OpenLoopLoadTest
{
    public static async Task RunAsync(LoadTestOptions options)
    {
        Console.WriteLine($"Open-loop load test | duration={options.DurationSeconds}s warmup={options.WarmupSeconds}s "
            + $"connections={options.Connections} pipeline={options.PipelineDepth} route={options.Route}");
        Console.WriteLine(new string('-', 88));

        var results = new List<LoadResult>();

        if (options.RunTurbo)
        {
            results.Add(await MeasureTurboAsync(options));
        }

        if (options.RunKestrel)
        {
            results.Add(await MeasureKestrelAsync(options));
        }

        PrintComparison(results);
    }

    private static async Task<LoadResult> MeasureTurboAsync(LoadTestOptions options)
    {
        var server = new TurboBenchmarkServer();
        await server.InitializeAsync();
        try
        {
            return await DriveAsync("TurboServer", server.Http11Port, options);
        }
        finally
        {
            await server.DisposeAsync();
        }
    }

    private static async Task<LoadResult> MeasureKestrelAsync(LoadTestOptions options)
    {
        var server = new BenchmarkServer();
        await server.InitializeAsync();
        try
        {
            return await DriveAsync("Kestrel", server.Http11Port, options);
        }
        finally
        {
            await server.DisposeAsync();
        }
    }

    private static async Task<LoadResult> DriveAsync(string name, int port, LoadTestOptions options)
    {
        var endpoint = new IPEndPoint(IPAddress.Loopback, port);
        var requestBlob = BuildPipelinedRequest(options.Route, options.PipelineDepth);
        var responseLength = await ProbeResponseLengthAsync(endpoint, options.Route);
        var batchBytes = responseLength * options.PipelineDepth;

        // Warmup (uncounted) lets the server JIT, fill pools, and reach steady state.
        if (options.WarmupSeconds > 0)
        {
            using var warmupCts = new CancellationTokenSource(TimeSpan.FromSeconds(options.WarmupSeconds));
            await RunPhaseAsync(endpoint, requestBlob, batchBytes, options, collectLatencies: false, warmupCts.Token);
        }

        var profiler = options.Profile ? new AllocationProfiler() : null;

        var gcBefore = (
            Alloc: GC.GetTotalAllocatedBytes(precise: true),
            Gen0: GC.CollectionCount(0),
            Gen1: GC.CollectionCount(1),
            Gen2: GC.CollectionCount(2));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(options.DurationSeconds));
        profiler?.Arm();
        var sw = Stopwatch.StartNew();
        var (requests, latencies) = await RunPhaseAsync(
            endpoint, requestBlob, batchBytes, options, collectLatencies: true, cts.Token);
        sw.Stop();
        profiler?.Disarm();

        var allocDelta = GC.GetTotalAllocatedBytes(precise: true) - gcBefore.Alloc;

        profiler?.Report(requests);

        latencies.Sort();
        return new LoadResult(
            name,
            requests,
            sw.Elapsed.TotalSeconds,
            requests / sw.Elapsed.TotalSeconds,
            Percentile(latencies, 0.50),
            Percentile(latencies, 0.99),
            requests == 0 ? 0 : (double)allocDelta / requests,
            GC.CollectionCount(0) - gcBefore.Gen0,
            GC.CollectionCount(1) - gcBefore.Gen1,
            GC.CollectionCount(2) - gcBefore.Gen2);
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
        Console.WriteLine($"{"Server",-14}{"Requests",12}{"RPS",14}{"P50 us",12}{"P99 us",12}{"Alloc B/req",14}{"GC 0/1/2",14}");
        Console.WriteLine(new string('-', 92));
        foreach (var r in results)
        {
            Console.WriteLine(
                $"{r.Name,-14}{r.Requests,12:N0}{r.RequestsPerSecond,14:N0}{r.P50Micros,12:N1}{r.P99Micros,12:N1}"
                + $"{r.AllocBytesPerRequest,14:N1}{$"{r.Gen0}/{r.Gen1}/{r.Gen2}",14}");
        }

        if (results.Count == 2)
        {
            var ratio = results[0].RequestsPerSecond / results[1].RequestsPerSecond;
            Console.WriteLine();
            Console.WriteLine($"{results[0].Name} RPS / {results[1].Name} RPS = {ratio:P1}");
        }

        Console.WriteLine();
        Console.WriteLine("Note: client+server share this process; alloc B/req includes client allocations.");
    }
}
