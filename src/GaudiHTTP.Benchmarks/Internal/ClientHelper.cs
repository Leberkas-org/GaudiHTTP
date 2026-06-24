using GaudiHTTP.Client;
using Akka.Actor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace GaudiHTTP.Benchmarks.Internal;

/// <summary>
/// Lightweight factory helper that creates <see cref="IGaudiHttpClient"/> instances
/// for benchmark use. Wraps the DI setup required by <see cref="GaudiHttpClientFactory"/>.
/// Each instance owns its own <see cref="ActorSystem"/> and terminates it on disposal,
/// preventing stale PinnedDispatcher threads from accumulating across BDN parameter combinations.
/// </summary>
internal sealed class ClientHelper : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly ActorSystem _system;

    private ClientHelper(ServiceProvider provider, IGaudiHttpClient client, ActorSystem system)
    {
        _provider = provider;
        Client = client;
        _system = system;
    }

    /// <summary>The configured <see cref="IGaudiHttpClient"/> instance.</summary>
    public IGaudiHttpClient Client { get; }

    /// <summary>
    /// Creates a new <see cref="ClientHelper"/> with a fully configured GaudiHttp client
    /// targeting the benchmark server for SendAsync benchmarks.
    /// </summary>
    /// <param name="baseAddress">The remote base URI (scheme + host).</param>
    /// <param name="version">The HTTP version to use.</param>
    public static ClientHelper CreateClient(Uri baseAddress, Version version, int? maxConnectionsOverride = null)
    {
        var options = new TurboClientOptions
        {
            BaseAddress = baseAddress,
            DangerousAcceptAnyServerCertificate = true,
            RequestBodyChunkSize = 64 * 1024,
            // H1.x: many connections with shallow pipelining to handle CL up to 8192.
            Http1 = new Http1ClientOptions
            {
                MaxConnectionsPerServer = maxConnectionsOverride ?? 512,
                MaxPipelineDepth = 64
            },
            // H2: 16 connections × 512 streams = 8192 in-flight capacity.
            // MaxConcurrentStreams must not exceed Kestrel's MaxStreamsPerConnection (512).
            Http2 = new Http2ClientOptions
            {
                MaxConnectionsPerServer = maxConnectionsOverride ?? 16,
                MaxConcurrentStreams = 512,
                MaxBufferedRequestBodySize = 2 * 1024 * 1024,
            },
            // H3: 64 connections × 100 streams = 6400 in-flight capacity.
            // MaxConcurrentStreams must match Kestrel's default (100) — exceeding it blocks
            // on QuicConnection.OpenOutboundStreamAsync until a stream is released.
            Http3 = new Http3ClientOptions
            {
                MaxConnectionsPerServer = maxConnectionsOverride ?? 64,
                MaxConcurrentStreams = 100,
                QpackMaxTableCapacity = 32_768,
                QpackBlockedStreams = 200,
                MaxFieldSectionSize = 65_536,
                IdleTimeout = TimeSpan.FromMinutes(5),
                MaxReconnectAttempts = 10,
                MaxReconnectBufferSize = 256,
                MaxBufferedRequestBodySize = 2 * 1024 * 1024,
            },
        };

        return Build(baseAddress, version, options);
    }

    /// <summary>
    /// Creates a new <see cref="ClientHelper"/> with streaming-optimised options
    /// targeting a remote URI for channel-based benchmarks.
    /// </summary>
    /// <param name="baseAddress">The remote base URI (scheme + host).</param>
    /// <param name="version">The HTTP version to use.</param>
    public static ClientHelper CreateStreamingClient(Uri baseAddress, Version version)
    {
        var options = new TurboClientOptions
        {
            BaseAddress = baseAddress,
            DangerousAcceptAnyServerCertificate = true,
            // Streaming H1.x: enough connections to saturate high-CL scenarios
            // (H1.1 is head-of-line blocked per connection, so depth alone doesn't help).
            Http1 = new Http1ClientOptions { MaxConnectionsPerServer = 128, MaxPipelineDepth = 64 },
            // H2: 16 connections × 1000 streams for high-CL streaming.
            Http2 = new Http2ClientOptions { MaxConnectionsPerServer = 16, MaxConcurrentStreams = 1000 },
            // H3: 64 connections × 100 streams — match Kestrel's MaxInboundBidirectionalStreams default.
            Http3 = new Http3ClientOptions
            {
                MaxConnectionsPerServer = 64,
                MaxConcurrentStreams = 100,
                QpackMaxTableCapacity = 32_768,
                QpackBlockedStreams = 200,
                MaxFieldSectionSize = 65_536,
                IdleTimeout = TimeSpan.FromMinutes(5),
                MaxReconnectAttempts = 10,
                MaxReconnectBufferSize = 256,
            },
            MaxConcurrentEndpoints = 16384,
        };

        return Build(baseAddress, version, options);
    }

    private static ClientHelper Build(Uri baseAddress, Version version, TurboClientOptions options)
    {
        var services = new ServiceCollection();

        // Create and register the ActorSystem explicitly so it can be terminated on disposal.
        // Without this, GaudiHttpClientFactory creates an untracked ActorSystem that is never
        // terminated, causing PinnedDispatcher threads to accumulate across BDN combinations.
        //
        // exit-clr = off: do not call Environment.Exit after CoordinatedShutdown completes.
        //   The BDN host process must outlive all parameter combinations; an exit here would
        //   kill the process mid-benchmark run.
        // run-by-clr-shutdown = off: do not hook AppDomain.ProcessExit. BDN's EventPipe
        //   profiler can trigger a CLR-shutdown-like event that would otherwise fire
        //   CoordinatedShutdown on ALL live ActorSystems in the process simultaneously,
        //   terminating the current combination's system mid-warmup.
        var hocon = Akka.Configuration.ConfigurationFactory.ParseString(@"
            akka.coordinated-shutdown.exit-clr = off
            akka.coordinated-shutdown.run-by-clr-shutdown = off
        ");
        var system = ActorSystem.Create($"GaudiHttp-bench-{Guid.NewGuid():N}", hocon);
        services.AddSingleton(system);

        services.AddGaudiHttpClient();
        services.Replace(ServiceDescriptor.Singleton<IOptionsFactory<TurboClientOptions>>(
            new FixedOptionsFactory(options)));

        var provider = services.BuildServiceProvider();

        var factory = provider.GetRequiredService<IGaudiHttpClientFactory>();
        var client = factory.CreateClient(string.Empty);
        client.BaseAddress = baseAddress;
        client.DefaultRequestVersion = version;
        client.Timeout = TimeSpan.FromMinutes(5);

        return new ClientHelper(provider, client, system);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // Signal pipeline to drain
        Client.Requests.TryComplete();

        try
        {
            await Client.Responses.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Pipeline may complete with an error during shutdown — that is fine.
        }

        Client.Dispose();

        // Terminate the ActorSystem to stop all PinnedDispatcher threads.
        // Without this, each BDN parameter combination leaks ~50–100 OS threads, causing
        // scheduling contention that inflates latency 13× by combination #13 (CL=64).
        //
        // Both WaitAsync calls are wrapped: under high concurrency (CL=4096) PinnedDispatcher
        // threads can take >10 s to drain, causing WaitAsync to throw TimeoutException.
        // An uncaught throw here skips _provider.DisposeAsync(), leaving the ServiceProvider
        // alive, which keeps the DI-registered ActorSystem from being disposed through the
        // container — resulting in a second live system that races into the next combination.
        try
        {
            await _system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch
        {
            // Termination is still in progress in the background; the system will finish
            // winding down on its own. Proceed so the ServiceProvider is always disposed.
        }

        try
        {
            await _system.WhenTerminated.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Same rationale: don't block indefinitely waiting for full termination.
        }

        // Allow dispatcher threads to fully wind down before the next combination starts.
        await Task.Delay(TimeSpan.FromMilliseconds(250));

        await _provider.DisposeAsync();
    }

    private sealed class FixedOptionsFactory(TurboClientOptions options) : IOptionsFactory<TurboClientOptions>
    {
        public TurboClientOptions Create(string name) => options;
    }
}