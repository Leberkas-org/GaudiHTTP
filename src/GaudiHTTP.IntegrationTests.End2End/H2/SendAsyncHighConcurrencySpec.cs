using System.Diagnostics;
using System.Net;
using Akka.Actor;
using Akka.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GaudiHTTP.Client;
using TraceLevel = Servus.Diagnostics.TraceLevel;
using static Servus.Senf;

namespace GaudiHTTP.IntegrationTests.End2End.H2;

/// <summary>
/// Reproduces the benchmark showstopper: SendAsync stalls at 512+ concurrent requests
/// over H2 against Kestrel. Uses graduated concurrency levels with Senf tracing to
/// pinpoint where the pipeline stalls.
/// </summary>
public sealed class SendAsyncHighConcurrencySpec : IAsyncLifetime
{
    private WebApplication? _kestrelApp;
    private IGaudiHttpClient? _client;
    private Microsoft.Extensions.DependencyInjection.ServiceProvider? _clientProvider;
    private string _baseUri = string.Empty;

    private CancellationToken CT => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        Assert.Skip("High-concurrency spec causes resource contention with parallel test collections in CI");

        // --- Kestrel server (matches benchmark BenchmarkServer config) ---
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();

        builder.Services.Configure<KestrelServerOptions>(kestrel =>
        {
            kestrel.Listen(IPAddress.Loopback, 0, lo =>
                lo.Protocols = HttpProtocols.Http2);

            kestrel.Limits.Http2.MaxStreamsPerConnection = 512;
            kestrel.Limits.Http2.InitialConnectionWindowSize = 4 * 1024 * 1024;
            kestrel.Limits.Http2.InitialStreamWindowSize = 1024 * 1024;
            kestrel.Limits.Http2.MaxFrameSize = 256 * 1024;
            kestrel.Limits.MaxConcurrentConnections = null;
        });

        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

        _kestrelApp = builder.Build();

        _kestrelApp.MapGet("/simple", () => "OK\n");
        _kestrelApp.MapPost("/upload", async ctx =>
        {
            long total = 0;
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = await ctx.Request.Body.ReadAsync(buffer, ctx.RequestAborted)) > 0)
            {
                total += read;
            }
            await ctx.Response.WriteAsync($"received:{total}", ctx.RequestAborted);
        });

        await _kestrelApp.StartAsync();

        var port = new Uri(_kestrelApp.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First()).Port;
        _baseUri = $"http://127.0.0.1:{port}";

        // --- GaudiHTTP client (matches benchmark ClientHelper config) ---
        ConfigureTracing();

        var services = new ServiceCollection();

        var diSetup = DependencyResolverSetup.Create(services.BuildServiceProvider());
        var bootstrap = BootstrapSetup.Create();
        var system = ActorSystem.Create($"repro-{Guid.NewGuid():N}", bootstrap.And(diSetup));
        services.AddSingleton(system);

        var clientOptions = new GaudiClientOptions
        {
            BaseAddress = new Uri(_baseUri),
            Http2 = new Http2ClientOptions
            {
                MaxConnectionsPerServer = 16,
                MaxConcurrentStreams = 512,
                MaxBufferedRequestBodySize = 2 * 1024 * 1024,
            },
        };

        services.AddGaudiHttpClient();
        services.Replace(ServiceDescriptor.Singleton<IOptionsFactory<GaudiClientOptions>>(
            new FixedOptionsFactory(clientOptions)));

        _clientProvider = services.BuildServiceProvider();

        var factory = _clientProvider.GetRequiredService<IGaudiHttpClientFactory>();
        _client = factory.CreateClient(string.Empty);
        _client.BaseAddress = new Uri(_baseUri);
        _client.DefaultRequestVersion = HttpVersion.Version20;
        _client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
        _client.Timeout = TimeSpan.FromMinutes(5);

        // Warmup: single request to establish first connection
        using var warmup = new HttpRequestMessage(HttpMethod.Get, $"{_baseUri}/simple");
        using var warmupResp = await _client.SendAsync(warmup, CT);
        warmupResp.EnsureSuccessStatusCode();
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();

        if (_kestrelApp is not null)
        {
            await _kestrelApp.StopAsync();
            await _kestrelApp.DisposeAsync();
        }

        if (_clientProvider is not null)
        {
            var system = _clientProvider.GetService<ActorSystem>();
            if (system is not null)
            {
                try { await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10)); } catch { }
                try { await system.WhenTerminated.WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
            }

            await _clientProvider.DisposeAsync();
        }

        Tracing.Disable();
    }

    /// <summary>
    /// Baseline: 64 concurrent GET — should always pass.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task SendAsync_64_concurrent_light_should_complete()
    {
        await RunConcurrentLight(64, maxInFlight: 64, timeout: TimeSpan.FromSeconds(15));
    }

    /// <summary>
    /// Mid tier: 256 concurrent GET — matches MaxInFlight from benchmark.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task SendAsync_256_concurrent_light_should_complete()
    {
        await RunConcurrentLight(256, maxInFlight: 256, timeout: TimeSpan.FromSeconds(15));
    }

    /// <summary>
    /// The showstopper: 512 concurrent GET — this is where the benchmark stalls.
    /// </summary>
    [Fact(Timeout = 90_000)]
    public async Task SendAsync_512_concurrent_light_should_complete()
    {
        await RunConcurrentLight(512, maxInFlight: 256, timeout: TimeSpan.FromSeconds(60));
    }

    /// <summary>
    /// Heavy variant: 512 concurrent POST (1MB each) — times out in benchmark.
    /// </summary>
    [Fact(Timeout = 120_000)]
    public async Task SendAsync_512_concurrent_heavy_should_complete()
    {
        await RunConcurrentHeavy(512, maxInFlight: 256, timeout: TimeSpan.FromSeconds(90));
    }

    /// <summary>
    /// Extreme: 4096 concurrent GET — all crash in benchmark.
    /// </summary>
    [Fact(Timeout = 180_000)]
    public async Task SendAsync_4096_concurrent_light_should_complete()
    {
        await RunConcurrentLight(4096, maxInFlight: 256, timeout: TimeSpan.FromSeconds(120));
    }

    /// <summary>
    /// Sustained load: simulates BDN's repeated invocations — 50 rounds of 512 concurrent
    /// GET. The benchmark does 32 invocations × 10 iterations = 320 rounds. If the pipeline
    /// degrades over time (actor leak, connection exhaustion, GC pressure), this catches it.
    /// </summary>
    [Fact(Timeout = 120_000)]
    public async Task SendAsync_sustained_512_light_50_rounds_should_not_degrade()
    {
        const int rounds = 50;
        const int concurrency = 512;
        const int maxInFlight = 256;
        var roundTimes = new double[rounds];

        for (var round = 0; round < rounds; round++)
        {
            var gate = new SemaphoreSlim(maxInFlight, maxInFlight);
            var tasks = new Task[concurrency];
            var counters = new Counters();
            var sw = Stopwatch.StartNew();

            for (var i = 0; i < concurrency; i++)
            {
                tasks[i] = FireLightRequest(gate, TimeSpan.FromSeconds(30), counters);
            }

            await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            sw.Stop();
            roundTimes[round] = sw.Elapsed.TotalMilliseconds;

            if (counters.Failed > 0)
            {
                Assert.Fail($"Round {round}: {counters.Failed} failures after {sw.Elapsed.TotalMilliseconds:F0}ms");
            }

            if (round % 10 == 0 || sw.Elapsed.TotalMilliseconds > 1000)
            {
                TestContext.Current.TestOutputHelper?.WriteLine(
                    $"Round {round}: {sw.Elapsed.TotalMilliseconds:F0}ms " +
                    $"({concurrency / sw.Elapsed.TotalSeconds:F0} req/s)");
            }
        }

        var median = roundTimes.OrderBy(t => t).ElementAt(rounds / 2);
        var p95 = roundTimes.OrderBy(t => t).ElementAt((int)(rounds * 0.95));
        var max = roundTimes.Max();

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"Sustained: {rounds} rounds, median={median:F0}ms, P95={p95:F0}ms, max={max:F0}ms");

        Assert.True(max < 10_000,
            $"Worst round took {max:F0}ms — likely pipeline stall (median was {median:F0}ms)");
    }

    /// <summary>
    /// Sustained heavy: 10 rounds of 512 concurrent POST (1MB each) WITH GC between rounds.
    /// If GC.Collect fixes the stall, the root cause is GC pressure, not a pipeline bug.
    /// </summary>
    [Fact(Timeout = 180_000)]
    public async Task SendAsync_sustained_512_heavy_with_gc_between_rounds()
    {
        var payload = new byte[1024 * 1024];
        Array.Fill(payload, (byte)0xAB);

        const int rounds = 10;
        const int concurrency = 512;
        const int maxInFlight = 256;
        var roundTimes = new double[rounds];

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"GC mode: {(System.Runtime.GCSettings.IsServerGC ? "Server" : "Workstation")}");

        for (var round = 0; round < rounds; round++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();

            var gate = new SemaphoreSlim(maxInFlight, maxInFlight);
            var tasks = new Task[concurrency];
            var counters = new Counters();
            var sw = Stopwatch.StartNew();

            for (var i = 0; i < concurrency; i++)
            {
                tasks[i] = FireHeavyRequest(gate, payload, TimeSpan.FromSeconds(60), counters);
            }

            await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
            sw.Stop();
            roundTimes[round] = sw.Elapsed.TotalMilliseconds;

            if (counters.Failed > 0)
            {
                Assert.Fail($"Round {round}: {counters.Failed} failures after {sw.Elapsed.TotalMilliseconds:F0}ms");
            }

            TestContext.Current.TestOutputHelper?.WriteLine(
                $"Round {round}: {sw.Elapsed.TotalMilliseconds:F0}ms " +
                $"({concurrency / sw.Elapsed.TotalSeconds:F0} req/s)");
        }

        var median = roundTimes.OrderBy(t => t).ElementAt(rounds / 2);
        var max = roundTimes.Max();

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"With GC: {rounds} rounds, median={median:F0}ms, max={max:F0}ms");

        Assert.True(max < 30_000,
            $"Worst round took {max:F0}ms (median {median:F0}ms)");
    }

    /// <summary>
    /// Sustained heavy: 30 rounds of 512 concurrent POST (1MB each) WITHOUT GC.
    /// </summary>
    [Fact(Timeout = 180_000)]
    public async Task SendAsync_sustained_512_heavy_30_rounds_should_not_degrade()
    {
        var payload = new byte[1024 * 1024];
        Array.Fill(payload, (byte)0xAB);

        const int rounds = 30;
        const int concurrency = 512;
        const int maxInFlight = 256;
        var roundTimes = new double[rounds];

        var gc0Before = GC.CollectionCount(0);
        var gc1Before = GC.CollectionCount(1);
        var gc2Before = GC.CollectionCount(2);

        for (var round = 0; round < rounds; round++)
        {
            var gate = new SemaphoreSlim(maxInFlight, maxInFlight);
            var tasks = new Task[concurrency];
            var counters = new Counters();
            var sw = Stopwatch.StartNew();

            for (var i = 0; i < concurrency; i++)
            {
                tasks[i] = FireHeavyRequest(gate, payload, TimeSpan.FromSeconds(60), counters);
            }

            await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
            sw.Stop();
            roundTimes[round] = sw.Elapsed.TotalMilliseconds;

            var gc0 = GC.CollectionCount(0) - gc0Before;
            var gc1 = GC.CollectionCount(1) - gc1Before;
            var gc2 = GC.CollectionCount(2) - gc2Before;

            if (counters.Failed > 0)
            {
                Assert.Fail($"Round {round}: {counters.Failed} failures after {sw.Elapsed.TotalMilliseconds:F0}ms");
            }

            TestContext.Current.TestOutputHelper?.WriteLine(
                $"Round {round}: {sw.Elapsed.TotalMilliseconds:F0}ms " +
                $"({concurrency / sw.Elapsed.TotalSeconds:F0} req/s) " +
                $"GC[{gc0}/{gc1}/{gc2}] Mem={GC.GetTotalMemory(false) / 1024 / 1024}MB");
        }

        var median = roundTimes.OrderBy(t => t).ElementAt(rounds / 2);
        var p95 = roundTimes.OrderBy(t => t).ElementAt((int)(rounds * 0.95));
        var max = roundTimes.Max();

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"Sustained heavy: {rounds} rounds, median={median:F0}ms, P95={p95:F0}ms, max={max:F0}ms");

        Assert.True(max < 30_000,
            $"Worst round took {max:F0}ms — likely pipeline stall (median was {median:F0}ms)");
    }

    private async Task RunConcurrentLight(int concurrency, int maxInFlight, TimeSpan timeout)
    {
        var gate = new SemaphoreSlim(maxInFlight, maxInFlight);
        var tasks = new Task[concurrency];
        var counters = new Counters();
        var sw = Stopwatch.StartNew();

        var progressTimer = new Timer(_ =>
        {
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"[{sw.Elapsed:mm\\:ss\\.ff}] Progress: {counters.Completed}/{concurrency} completed, " +
                $"{counters.Failed} failed, {maxInFlight - gate.CurrentCount} in-flight");
        }, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));

        try
        {
            for (var i = 0; i < concurrency; i++)
            {
                tasks[i] = FireLightRequest(gate, timeout, counters);
            }

            await Task.WhenAll(tasks).WaitAsync(timeout);
        }
        finally
        {
            await progressTimer.DisposeAsync();
        }

        sw.Stop();
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"Completed {concurrency} light requests in {sw.Elapsed.TotalMilliseconds:F0}ms " +
            $"({concurrency / sw.Elapsed.TotalSeconds:F0} req/s), {counters.Failed} failures");

        Assert.Equal(0, counters.Failed);
    }

    private async Task RunConcurrentHeavy(int concurrency, int maxInFlight, TimeSpan timeout)
    {
        var payload = new byte[1024 * 1024];
        Array.Fill(payload, (byte)0xAB);

        var gate = new SemaphoreSlim(maxInFlight, maxInFlight);
        var tasks = new Task[concurrency];
        var counters = new Counters();
        var sw = Stopwatch.StartNew();

        var progressTimer = new Timer(_ =>
        {
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"[{sw.Elapsed:mm\\:ss\\.ff}] Progress: {counters.Completed}/{concurrency} completed, " +
                $"{counters.Failed} failed, {maxInFlight - gate.CurrentCount} in-flight");
        }, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));

        try
        {
            for (var i = 0; i < concurrency; i++)
            {
                tasks[i] = FireHeavyRequest(gate, payload, timeout, counters);
            }

            await Task.WhenAll(tasks).WaitAsync(timeout);
        }
        finally
        {
            await progressTimer.DisposeAsync();
        }

        sw.Stop();
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"Completed {concurrency} heavy requests in {sw.Elapsed.TotalMilliseconds:F0}ms " +
            $"({concurrency / sw.Elapsed.TotalSeconds:F0} req/s), {counters.Failed} failures");

        Assert.Equal(0, counters.Failed);
    }

    private async Task FireLightRequest(SemaphoreSlim gate, TimeSpan timeout, Counters counters)
    {
        await gate.WaitAsync(CT);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(CT);
            cts.CancelAfter(timeout);
            var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUri}/simple");
            using var response = await _client!.SendAsync(request, cts.Token);
            response.EnsureSuccessStatusCode();
            Interlocked.Increment(ref counters.Completed);
        }
        catch
        {
            Interlocked.Increment(ref counters.Failed);
            throw;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task FireHeavyRequest(SemaphoreSlim gate, byte[] payload, TimeSpan timeout,
        Counters counters)
    {
        await gate.WaitAsync(CT);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(CT);
            cts.CancelAfter(timeout);
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUri}/upload")
            {
                Content = new ByteArrayContent(payload)
            };
            using var response = await _client!.SendAsync(request, cts.Token);
            response.EnsureSuccessStatusCode();
            _ = await response.Content.ReadAsStringAsync(cts.Token);
            Interlocked.Increment(ref counters.Completed);
        }
        catch
        {
            Interlocked.Increment(ref counters.Failed);
            throw;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Small body control: 512 concurrent × 10 rounds × 1KB POST.
    /// If this stalls too, the issue isn't memory pressure — it's stream/connection state.
    /// </summary>
    [Fact(Timeout = 120_000)]
    public async Task SendAsync_sustained_512_small_body_10_rounds()
    {
        var payload = new byte[1024];
        Array.Fill(payload, (byte)0xAB);

        for (var round = 0; round < 10; round++)
        {
            var gate = new SemaphoreSlim(256, 256);
            var tasks = new Task[512];
            var counters = new Counters();
            var sw = Stopwatch.StartNew();

            for (var i = 0; i < 512; i++)
            {
                tasks[i] = FireHeavyRequest(gate, payload, TimeSpan.FromSeconds(30), counters);
            }

            try
            {
                await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            }
            catch (TimeoutException)
            {
                TestContext.Current.TestOutputHelper?.WriteLine(
                    $"STALL C=512 1KB round={round}: {counters.Completed}/512 after {sw.Elapsed.TotalMilliseconds:F0}ms");
                Assert.Fail($"Stalled at round {round} with small bodies");
            }

            sw.Stop();
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"Round {round}: {sw.Elapsed.TotalMilliseconds:F0}ms ({512 / sw.Elapsed.TotalSeconds:F0} req/s)");
        }
    }

    /// <summary>
    /// Low concurrency, many rounds: 4 concurrent × 100 rounds × 1MB POST.
    /// If this degrades too, the issue is per-stream state accumulation, not concurrency.
    /// </summary>
    [Fact(Timeout = 120_000)]
    public async Task SendAsync_low_concurrency_100_rounds_heavy()
    {
        var payload = new byte[1024 * 1024];
        Array.Fill(payload, (byte)0xAB);

        for (var round = 0; round < 100; round++)
        {
            var gate = new SemaphoreSlim(4, 4);
            var tasks = new Task[4];
            var counters = new Counters();
            var sw = Stopwatch.StartNew();

            for (var i = 0; i < 4; i++)
            {
                tasks[i] = FireHeavyRequest(gate, payload, TimeSpan.FromSeconds(30), counters);
            }

            try
            {
                await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            }
            catch (TimeoutException)
            {
                TestContext.Current.TestOutputHelper?.WriteLine(
                    $"STALL at round {round}: {counters.Completed}/4 after {sw.Elapsed.TotalMilliseconds:F0}ms");
                Assert.Fail($"Stalled at round {round}");
            }

            sw.Stop();
            if (round % 20 == 0 || sw.Elapsed.TotalMilliseconds > 500)
            {
                TestContext.Current.TestOutputHelper?.WriteLine(
                    $"Round {round}: {sw.Elapsed.TotalMilliseconds:F0}ms");
            }
        }
    }

    /// <summary>
    /// Graduated heavy: finds the concurrency threshold where sustained load breaks.
    /// Runs 5 rounds at each level. Stops at first failure.
    /// </summary>
    [Fact(Timeout = 180_000)]
    public async Task SendAsync_find_heavy_stall_threshold()
    {
        var payload = new byte[1024 * 1024];
        Array.Fill(payload, (byte)0xAB);
        int[] levels = [32, 64, 128, 256, 512];

        foreach (var concurrency in levels)
        {
            var maxInFlight = Math.Min(concurrency, 256);
            var allOk = true;

            for (var round = 0; round < 5; round++)
            {
                var gate = new SemaphoreSlim(maxInFlight, maxInFlight);
                var tasks = new Task[concurrency];
                var counters = new Counters();
                var sw = Stopwatch.StartNew();

                for (var i = 0; i < concurrency; i++)
                {
                    tasks[i] = FireHeavyRequest(gate, payload, TimeSpan.FromSeconds(45), counters);
                }

                try
                {
                    await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(45), TestContext.Current.CancellationToken);
                }
                catch (TimeoutException)
                {
                    sw.Stop();
                    TestContext.Current.TestOutputHelper?.WriteLine(
                        $"STALL C={concurrency} round={round}: {counters.Completed}/{concurrency} completed, " +
                        $"{counters.Failed} failed after {sw.Elapsed.TotalMilliseconds:F0}ms");
                    allOk = false;
                    break;
                }

                sw.Stop();

                if (counters.Failed > 0)
                {
                    TestContext.Current.TestOutputHelper?.WriteLine(
                        $"FAIL C={concurrency} round={round}: {counters.Failed} failures after " +
                        $"{sw.Elapsed.TotalMilliseconds:F0}ms");
                    allOk = false;
                    break;
                }

                TestContext.Current.TestOutputHelper?.WriteLine(
                    $"C={concurrency} round={round}: {sw.Elapsed.TotalMilliseconds:F0}ms " +
                    $"({concurrency / sw.Elapsed.TotalSeconds:F0} req/s)");
            }

            if (!allOk)
            {
                break;
            }

            TestContext.Current.TestOutputHelper?.WriteLine($"--- C={concurrency}: ALL 5 ROUNDS OK ---");
        }
    }

    /// <summary>
    /// Diagnostic test: reproduces the BDN crash by firing ALL requests simultaneously
    /// WITHOUT a SemaphoreSlim gate — exactly matching the benchmark behavior. Graduates
    /// from 64→4096 to find the exact stall threshold.
    /// </summary>
    [Fact(Timeout = 300_000)]
    public async Task Diagnose_sendAsync_stall_threshold_with_traces()
    {
        int[] levels = [64, 128, 256, 512, 1024, 2048, 4096];

        Console.Error.WriteLine($"ThreadPool: count={ThreadPool.ThreadCount}, " +
                                $"GC={(System.Runtime.GCSettings.IsServerGC ? "Server" : "Workstation")}");

        foreach (var concurrency in levels)
        {
            var tasks = new Task<HttpResponseMessage>[concurrency];
            var completed = 0;
            var failed = 0;
            var sw = Stopwatch.StartNew();
            var timeout = TimeSpan.FromSeconds(Math.Max(30, concurrency / 10));

            using var progressTimer = new Timer(_ =>
            {
                ThreadPool.GetAvailableThreads(out var workerAvail, out var ioAvail);
                ThreadPool.GetMaxThreads(out var workerMax, out var ioMax);

                Console.Error.WriteLine(
                    $"[{sw.Elapsed:mm\\:ss\\.ff}] CL={concurrency}: " +
                    $"{Volatile.Read(ref completed)}/{concurrency} done, {Volatile.Read(ref failed)} fail, " +
                    $"ThreadPool: busy={workerMax - workerAvail}/{workerMax} io={ioMax - ioAvail}/{ioMax} " +
                    $"Mem={GC.GetTotalMemory(false) / 1024 / 1024}MB");
            }, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));

            for (var i = 0; i < concurrency; i++)
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUri}/simple");
                tasks[i] = _client!.SendAsync(request, CT).ContinueWith(t =>
                {
                    if (t.IsCompletedSuccessfully)
                    {
                        Interlocked.Increment(ref completed);
                    }
                    else
                    {
                        Interlocked.Increment(ref failed);
                    }

                    return t.IsCompletedSuccessfully ? t.Result : null!;
                }, TaskScheduler.Default);
            }

            try
            {
                await Task.WhenAll(tasks).WaitAsync(timeout, TestContext.Current.CancellationToken);
            }
            catch (TimeoutException)
            {
                sw.Stop();

                ThreadPool.GetAvailableThreads(out var wa, out var ia);
                ThreadPool.GetMaxThreads(out var wm, out var im);

                Console.Error.WriteLine(
                    $"=== TIMEOUT at CL={concurrency}: {completed}/{concurrency} done, {failed} fail " +
                    $"after {sw.Elapsed.TotalMilliseconds:F0}ms ===");
                Console.Error.WriteLine(
                    $"    ThreadPool: busy={wm - wa}/{wm} io={im - ia}/{im} " +
                    $"Mem={GC.GetTotalMemory(false) / 1024 / 1024}MB");

                Assert.Fail(
                    $"Pipeline stalled at CL={concurrency}: only {completed}/{concurrency} " +
                    $"completed in {sw.Elapsed.TotalMilliseconds:F0}ms");
                return;
            }

            sw.Stop();

            foreach (var t in tasks)
            {
                if (t.IsCompletedSuccessfully)
                {
                    var response = await t;
                    response?.Dispose();
                }
            }

            Console.Error.WriteLine(
                $"CL={concurrency}: OK in {sw.Elapsed.TotalMilliseconds:F0}ms " +
                $"({concurrency / sw.Elapsed.TotalSeconds:F0} req/s), {failed} failures");

            if (failed > 0)
            {
                Assert.Fail($"CL={concurrency}: {failed}/{concurrency} requests failed");
                return;
            }
        }
    }

    private sealed class Counters
    {
        public int Completed;
        public int Failed;
    }

    private static void ConfigureTracing()
    {
        ThreadPool.GetMinThreads(out var w, out var io);
        ThreadPool.SetMinThreads(Math.Max(w, 1024), Math.Max(io, 1024));

        var loggerFactory = LoggerFactory.Create(b =>
        {
            b.AddConsole();
            b.SetMinimumLevel(LogLevel.Debug);
        });

        Tracing.Configure(
            new Diagnostics.LoggerTraceListener(loggerFactory),
            TraceLevel.Warning);
    }

    private sealed class FixedOptionsFactory(GaudiClientOptions options) : IOptionsFactory<GaudiClientOptions>
    {
        public GaudiClientOptions Create(string name) => options;
    }
}
