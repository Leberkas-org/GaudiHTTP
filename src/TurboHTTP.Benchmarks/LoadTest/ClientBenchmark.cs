using System.Diagnostics;
using System.Net;
using System.Net.Http;
using TurboHTTP.Benchmarks.Internal;

namespace TurboHTTP.Benchmarks.LoadTest;

internal static class ClientBenchmark
{
    public static async Task RunAsync(LoadTestOptions options)
    {
        var protocol = options.Protocol;
        var httpVersion = protocol switch
        {
            "client-h2" => HttpVersion.Version20,
            "client-h3" => HttpVersion.Version30,
            _ => HttpVersion.Version11,
        };
        var scheme = protocol == "client-h3" ? "https" : "http";

        Console.WriteLine(string.Concat("Client benchmark | protocol=", protocol,
            " duration=", options.DurationSeconds.ToString(), "s warmup=", options.WarmupSeconds.ToString(),
            "s connections=", options.Connections.ToString(), " concurrency=", options.PipelineDepth.ToString()));
        Console.WriteLine(new string('-', 80));

        var child = StartKestrelServer();
        try
        {
            var ports = await ReadPortsAsync(child);
            var port = protocol switch
            {
                "client-h2" => ports.H2,
                "client-h3" => ports.H3,
                _ => ports.H1,
            };

            var baseUri = new Uri(string.Concat(scheme, "://127.0.0.1:", port.ToString()));
            var route = options.Route;

            Console.WriteLine("Kestrel server on port " + port.ToString());

            var turboResult = await MeasureTurboAsync(baseUri, httpVersion, route, options);
            var dotnetResult = await MeasureDotnetAsync(baseUri, httpVersion, route, options);

            Console.WriteLine();
            Console.WriteLine($"{"Client",-16}{"Requests",14}{"RPS",14}{"P50 us",12}{"P99 us",12}{"B/req",12}{"GC 0/1/2",14}");
            Console.WriteLine(new string('-', 94));
            PrintRow("TurboHTTP", turboResult);
            PrintRow("HttpClient", dotnetResult);
            Console.WriteLine();

            if (dotnetResult.Rps > 0)
            {
                Console.WriteLine(string.Concat("TurboHTTP RPS / HttpClient RPS = ",
                    (turboResult.Rps / dotnetResult.Rps * 100).ToString("F1"), "%"));
            }
        }
        finally
        {
            KillChild(child);
        }
    }

    private static void PrintRow(string name, ClientResult r)
    {
        Console.WriteLine($"{name,-16}{r.Requests,14:N0}{r.Rps,14:N0}{r.P50,12:N1}{r.P99,12:N1}{r.BPerReq,12:N1}{r.Gc0,6}/{r.Gc1}/{r.Gc2}");
    }

    private static async Task<ClientResult> MeasureTurboAsync(
        Uri baseUri, Version httpVersion, string route, LoadTestOptions options)
    {
        Console.WriteLine("Starting TurboHTTP client...");
        await using var helper = ClientHelper.CreateClient(baseUri, httpVersion,
            maxConnectionsOverride: options.Connections);
        var client = helper.Client;

        var fullUri = new Uri(baseUri, route);
        var concurrency = options.Connections * options.PipelineDepth;

        if (options.WarmupSeconds > 0)
        {
            using var warmupCts = new CancellationTokenSource(TimeSpan.FromSeconds(options.WarmupSeconds));
            await RunTurboPhase(client, fullUri, concurrency, warmupCts.Token);
        }

        var allocBefore = GC.GetTotalAllocatedBytes(precise: true);
        var gcBefore = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(options.DurationSeconds));
        var sw = Stopwatch.StartNew();
        var (requests, latencies) = await RunTurboPhase(client, fullUri, concurrency, cts.Token);
        sw.Stop();

        var allocAfter = GC.GetTotalAllocatedBytes(precise: true);
        var gcAfter = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));

        latencies.Sort();
        return new ClientResult(
            requests, requests / sw.Elapsed.TotalSeconds,
            Percentile(latencies, 0.50), Percentile(latencies, 0.99),
            requests == 0 ? 0 : (double)(allocAfter - allocBefore) / requests,
            gcAfter.Item1 - gcBefore.Item1, gcAfter.Item2 - gcBefore.Item2, gcAfter.Item3 - gcBefore.Item3);
    }

    private static async Task<(long Requests, List<double> Latencies)> RunTurboPhase(
        TurboHTTP.Client.ITurboHttpClient client, Uri uri, int concurrency, CancellationToken ct)
    {
        long totalRequests = 0;
        var allLatencies = new List<double>();
        var workers = new Task<List<double>>[concurrency];

        for (var i = 0; i < concurrency; i++)
        {
            workers[i] = Task.Run(async () =>
            {
                var lats = new List<double>(4096);
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        var start = Stopwatch.GetTimestamp();
                        var req = new HttpRequestMessage(HttpMethod.Get, uri);
                        using var resp = await client.SendAsync(req, ct);
                        var body = await resp.Content.ReadAsByteArrayAsync(ct);
                        Interlocked.Increment(ref totalRequests);
                        lats.Add(Stopwatch.GetElapsedTime(start).TotalMicroseconds);
                    }
                }
                catch (OperationCanceledException) { }
                catch (HttpRequestException) { }

                return lats;
            });
        }

        await Task.WhenAll(workers);
        foreach (var w in workers)
        {
            allLatencies.AddRange(w.Result);
        }

        return (totalRequests, allLatencies);
    }

    private static async Task<ClientResult> MeasureDotnetAsync(
        Uri baseUri, Version httpVersion, string route, LoadTestOptions options)
    {
        Console.WriteLine("Starting .NET HttpClient...");

        var concurrency = options.Connections * options.PipelineDepth;
        var handlers = new List<SocketsHttpHandler>();
        var clients = new List<HttpClient>();

        for (var c = 0; c < options.Connections; c++)
        {
            var handler = new SocketsHttpHandler
            {
                MaxConnectionsPerServer = 1,
                EnableMultipleHttp2Connections = false,
                SslOptions = { RemoteCertificateValidationCallback = (_, _, _, _) => true },
            };
            handlers.Add(handler);
            clients.Add(new HttpClient(handler, disposeHandler: false)
            {
                BaseAddress = baseUri,
                DefaultRequestVersion = httpVersion,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
            });
        }

        var fullUri = new Uri(baseUri, route);

        if (options.WarmupSeconds > 0)
        {
            using var warmupCts = new CancellationTokenSource(TimeSpan.FromSeconds(options.WarmupSeconds));
            await RunDotnetPhase(clients, fullUri, options.PipelineDepth, warmupCts.Token);
        }

        var allocBefore = GC.GetTotalAllocatedBytes(precise: true);
        var gcBefore = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(options.DurationSeconds));
        var sw = Stopwatch.StartNew();
        var (requests, latencies) = await RunDotnetPhase(clients, fullUri, options.PipelineDepth, cts.Token);
        sw.Stop();

        var allocAfter = GC.GetTotalAllocatedBytes(precise: true);
        var gcAfter = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));

        foreach (var c in clients) { c.Dispose(); }
        foreach (var h in handlers) { h.Dispose(); }

        latencies.Sort();
        return new ClientResult(
            requests, requests / sw.Elapsed.TotalSeconds,
            Percentile(latencies, 0.50), Percentile(latencies, 0.99),
            requests == 0 ? 0 : (double)(allocAfter - allocBefore) / requests,
            gcAfter.Item1 - gcBefore.Item1, gcAfter.Item2 - gcBefore.Item2, gcAfter.Item3 - gcBefore.Item3);
    }

    private static async Task<(long Requests, List<double> Latencies)> RunDotnetPhase(
        List<HttpClient> clients, Uri uri, int concurrencyPerClient, CancellationToken ct)
    {
        long totalRequests = 0;
        var allLatencies = new List<double>();
        var workers = new Task<List<double>>[clients.Count * concurrencyPerClient];

        for (var c = 0; c < clients.Count; c++)
        {
            var client = clients[c];
            for (var s = 0; s < concurrencyPerClient; s++)
            {
                var idx = c * concurrencyPerClient + s;
                workers[idx] = Task.Run(async () =>
                {
                    var lats = new List<double>(4096);
                    try
                    {
                        while (!ct.IsCancellationRequested)
                        {
                            var start = Stopwatch.GetTimestamp();
                            using var resp = await client.GetAsync(uri, ct);
                            await resp.Content.ReadAsByteArrayAsync(ct);
                            Interlocked.Increment(ref totalRequests);
                            lats.Add(Stopwatch.GetElapsedTime(start).TotalMicroseconds);
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (HttpRequestException) { }

                    return lats;
                });
            }
        }

        await Task.WhenAll(workers);
        foreach (var w in workers)
        {
            allLatencies.AddRange(w.Result);
        }

        return (totalRequests, allLatencies);
    }

    private static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0) { return 0; }
        var idx = (int)(p * (sorted.Count - 1));
        return sorted[idx];
    }

    private static System.Diagnostics.Process StartKestrelServer()
    {
        var dll = System.Reflection.Assembly.GetExecutingAssembly().Location;
        var dotnet = Environment.ProcessPath ?? "dotnet";
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = dotnet,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        if (Path.GetFileNameWithoutExtension(dotnet).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            psi.ArgumentList.Add("exec");
            psi.ArgumentList.Add(dll);
        }

        psi.ArgumentList.Add("loadtest");
        psi.ArgumentList.Add("--serve");
        psi.ArgumentList.Add("kestrel");

        return System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start Kestrel server.");
    }

    private static async Task<(int H1, int H2, int H3)> ReadPortsAsync(System.Diagnostics.Process child)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        while (!timeout.IsCancellationRequested)
        {
            var line = await child.StandardOutput.ReadLineAsync(timeout.Token);
            if (line is null) { throw new InvalidOperationException("Kestrel exited early."); }

            if (line.StartsWith("PORT=", StringComparison.Ordinal))
            {
                var parts = line["PORT=".Length..].Split(';');
                var h1 = int.Parse(parts[0]);
                var h2 = parts.Length > 1 && parts[1].StartsWith("H2=", StringComparison.Ordinal)
                    ? int.Parse(parts[1]["H2=".Length..]) : h1;
                var h3 = parts.Length > 2 && parts[2].StartsWith("H3=", StringComparison.Ordinal)
                    ? int.Parse(parts[2]["H3=".Length..]) : h1;
                return (h1, h2, h3);
            }
        }

        throw new TimeoutException("Timeout waiting for Kestrel.");
    }

    private static void KillChild(System.Diagnostics.Process child)
    {
        try { if (!child.HasExited) { child.Kill(entireProcessTree: true); child.WaitForExit(TimeSpan.FromSeconds(5)); } }
        catch (InvalidOperationException) { }
        finally { child.Dispose(); }
    }

    private readonly record struct ClientResult(
        long Requests, double Rps, double P50, double P99,
        double BPerReq, int Gc0, int Gc1, int Gc2);
}
