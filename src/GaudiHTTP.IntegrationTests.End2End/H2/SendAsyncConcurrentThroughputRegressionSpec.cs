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

namespace GaudiHTTP.IntegrationTests.End2End.H2;

/// <summary>
/// Regression guard for the client-side throughput collapse introduced by commit 50918ff2
/// ("perf: round 4 — lock-free pending list"). That change replaced the O(1) ConcurrentDictionary
/// tracking in-flight <c>SendAsync</c> calls with a hand-rolled lock-free linked list whose
/// per-request removal is O(N); under high concurrency it becomes an O(N^2) walk plus a single-point
/// CompareExchange storm on the list head, burning CPU while throughput collapses (~60K -> ~0.3K rps
/// at 512 concurrent H2 streams).
///
/// The guard drives sustained 512-concurrent SendAsync against a localhost Kestrel h2c server and
/// requires the batch to finish well within a deadline. With the bug the batch needs tens of seconds
/// (or stalls); with O(1) tracking it finishes in ~1s of request time. Server is Kestrel (not
/// GaudiServer) so the measurement isolates the client.
/// </summary>
public sealed class SendAsyncConcurrentThroughputRegressionSpec : IAsyncLifetime
{
    private const int Concurrency = 512;
    private const int TotalRequests = 20_000;
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    private WebApplication? _kestrelApp;
    private IGaudiHttpClient? _client;
    private Microsoft.Extensions.DependencyInjection.ServiceProvider? _clientProvider;
    private string _baseUri = string.Empty;

    private CancellationToken CT => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Services.Configure<KestrelServerOptions>(kestrel =>
        {
            kestrel.Listen(IPAddress.Loopback, 0, lo => lo.Protocols = HttpProtocols.Http2);
            kestrel.Limits.Http2.MaxStreamsPerConnection = 512;
            kestrel.Limits.MaxConcurrentConnections = null;
        });

        _kestrelApp = builder.Build();
        _kestrelApp.MapGet("/simple", () => "OK\n");
        await _kestrelApp.StartAsync();

        var port = new Uri(_kestrelApp.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First()).Port;
        _baseUri = $"http://127.0.0.1:{port}";

        var services = new ServiceCollection();
        var diSetup = DependencyResolverSetup.Create(services.BuildServiceProvider());
        var system = ActorSystem.Create($"regress-{Guid.NewGuid():N}", BootstrapSetup.Create().And(diSetup));
        services.AddSingleton(system);

        var clientOptions = new GaudiClientOptions
        {
            BaseAddress = new Uri(_baseUri),
            Http2 = new Http2ClientOptions
            {
                MaxConnectionsPerServer = 16,
                MaxConcurrentStreams = 512,
            },
        };

        services.AddGaudiHttpClient();
        services.Replace(ServiceDescriptor.Singleton<IOptionsFactory<GaudiClientOptions>>(
            new FixedOptionsFactory(clientOptions)));
        _clientProvider = services.BuildServiceProvider();

        _client = _clientProvider.GetRequiredService<IGaudiHttpClientFactory>().CreateClient(string.Empty);
        _client.BaseAddress = new Uri(_baseUri);
        _client.DefaultRequestVersion = HttpVersion.Version20;
        _client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
        _client.Timeout = TimeSpan.FromSeconds(30);

        // Warm the first connection so the measured batch excludes connection establishment.
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
    }

    [Fact(Timeout = 60_000)]
    public async Task SendAsync_under_sustained_512_concurrency_should_not_collapse()
    {
        var dispatched = 0;
        var completed = 0;
        var failed = 0;
        var sw = Stopwatch.StartNew();

        async Task Worker()
        {
            while (Interlocked.Increment(ref dispatched) <= TotalRequests)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUri}/simple");
                    using var response = await _client!.SendAsync(request, CT);
                    response.EnsureSuccessStatusCode();
                    Interlocked.Increment(ref completed);
                }
                catch
                {
                    Interlocked.Increment(ref failed);
                }
            }
        }

        var workers = new Task[Concurrency];
        for (var i = 0; i < Concurrency; i++)
        {
            workers[i] = Worker();
        }

        try
        {
            await Task.WhenAll(workers).WaitAsync(Deadline, CT);
        }
        catch (TimeoutException)
        {
            Assert.Fail(
                $"Sustained {Concurrency}-concurrent SendAsync completed only {completed}/{TotalRequests} " +
                $"within {Deadline.TotalSeconds:F0}s ({completed / sw.Elapsed.TotalSeconds:F0} rps) — " +
                $"in-flight request-tracking contention regression (lock-free pending list, commit 50918ff2).");
        }

        sw.Stop();
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{TotalRequests} requests @ {Concurrency} concurrent in {sw.Elapsed.TotalMilliseconds:F0}ms " +
            $"({TotalRequests / sw.Elapsed.TotalSeconds:F0} rps)");

        Assert.Equal(0, failed);
        Assert.Equal(TotalRequests, completed);
    }

    private sealed class FixedOptionsFactory(GaudiClientOptions options) : IOptionsFactory<GaudiClientOptions>
    {
        public GaudiClientOptions Create(string name) => options;
    }
}
