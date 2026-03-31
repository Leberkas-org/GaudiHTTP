using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace TurboHttp.Benchmarks;

/// <summary>
/// Lightweight factory helper that creates <see cref="ITurboHttpClient"/> instances
/// for benchmark use. Wraps the DI setup required by <see cref="TurboHttpClientFactory"/>.
/// </summary>
internal sealed class ClientHelper : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly ITurboHttpClient _client;

    private ClientHelper(ServiceProvider provider, ITurboHttpClient client)
    {
        _provider = provider;
        _client = client;
    }

    /// <summary>The configured <see cref="ITurboHttpClient"/> instance.</summary>
    public ITurboHttpClient Client => _client;

    /// <summary>
    /// Creates a new <see cref="ClientHelper"/> with a fully configured TurboHttp client.
    /// </summary>
    /// <param name="port">The port the test server is listening on.</param>
    /// <param name="version">The HTTP version to use (e.g. <c>new Version(1, 1)</c>).</param>
    public static ClientHelper CreateClient(int port, Version version)
    {
        var services = new ServiceCollection();

        var options = new TurboClientOptions
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
            DangerousAcceptAnyServerCertificate = true
        };

        services.AddTurboHttpClient();
        services.Replace(ServiceDescriptor.Singleton<IOptionsFactory<TurboClientOptions>>(
            new FixedOptionsFactory(options)));

        var provider = services.BuildServiceProvider();

        var factory = provider.GetRequiredService<ITurboHttpClientFactory>();
        var client = factory.CreateClient(string.Empty);
        client.BaseAddress = options.BaseAddress;
        client.DefaultRequestVersion = version;
        client.Timeout = TimeSpan.FromMinutes(5);

        return new ClientHelper(provider, client);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // Signal pipeline to drain
        _client.Requests.TryComplete();

        try
        {
            await _client.Responses.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Pipeline may complete with an error during shutdown — that is fine.
        }

        _client.Dispose();
        await _provider.DisposeAsync();
    }

    private sealed class FixedOptionsFactory(TurboClientOptions options) : IOptionsFactory<TurboClientOptions>
    {
        public TurboClientOptions Create(string name) => options;
    }
}

/// <summary>
/// Comparative benchmarks measuring <see cref="ITurboHttpClient"/> performance for a
/// single sequential request across light (no body) and heavy (10 KB body) payloads.
/// Parameterized by HTTP version (1.1 and 2.0).
/// </summary>
[MemoryDiagnoser]
[WarmupCount(3)]
[IterationCount(5)]
[InvocationCount(32)]
public sealed class TurboHttpSingleRequestBenchmarks : BenchmarkBaseClass
{
    private ClientHelper _clientHelper = null!;
    private static readonly byte[] HeavyPayload = GeneratePayload(10 * 1024);

    /// <summary>
    /// Creates an <see cref="ITurboHttpClient"/> via <see cref="ClientHelper.CreateClient"/>
    /// configured for the current HTTP version, then warms it up with a single request.
    /// </summary>
    [GlobalSetup]
    public override void GlobalSetup()
    {
        base.GlobalSetup();
        _clientHelper = ClientHelper.CreateClient(KestrelPort, HttpVersionValue);
        WarmupRequest().GetAwaiter().GetResult();
    }

    /// <summary>Disposes the <see cref="ITurboHttpClient"/> and tears down the shared server.</summary>
    [GlobalCleanup]
    public override async Task GlobalCleanup()
    {
        await _clientHelper.DisposeAsync();
        await base.GlobalCleanup();
    }

    /// <inheritdoc />
    public override async Task WarmupRequest()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/benchmark/simple");
        using var response = await _clientHelper.Client.SendAsync(request, CancellationToken.None);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Issues a single GET request to <c>/benchmark/simple</c> and discards the response.
    /// Measures per-request overhead for the minimal (no body) payload scenario.
    /// </summary>
    [Benchmark]
    public async Task SingleRequest_Light()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/benchmark/simple");
        using var response = await _clientHelper.Client.SendAsync(request, CancellationToken.None);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Issues a single POST request with a 10 KB body to <c>/benchmark/payload</c>.
    /// Measures per-request overhead for the heavy payload scenario.
    /// </summary>
    [Benchmark]
    public async Task SingleRequest_Heavy()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/benchmark/payload")
        {
            Content = new ByteArrayContent(HeavyPayload)
        };
        using var response = await _clientHelper.Client.SendAsync(request, CancellationToken.None);
        response.EnsureSuccessStatusCode();
    }
}

/// <summary>
/// Comparative benchmarks measuring <see cref="ITurboHttpClient"/> performance under concurrent
/// load. N requests are fired concurrently using <see cref="Task.WhenAll"/> and awaited as a unit.
/// Parameterized by <see cref="BenchmarkBaseClass.ConcurrencyLevel"/> and HTTP version.
/// </summary>
[MemoryDiagnoser]
[WarmupCount(3)]
[IterationCount(5)]
[InvocationCount(32)]
public sealed class TurboHttpConcurrentBenchmarks : BenchmarkBaseClass
{
    private ClientHelper _clientHelper = null!;
    private static readonly byte[] HeavyPayload = GeneratePayload(10 * 1024);

    /// <summary>
    /// Creates an <see cref="ITurboHttpClient"/> via <see cref="ClientHelper.CreateClient"/>
    /// configured for the current HTTP version, then warms it up with a single request.
    /// </summary>
    [GlobalSetup]
    public override void GlobalSetup()
    {
        base.GlobalSetup();
        _clientHelper = ClientHelper.CreateClient(KestrelPort, HttpVersionValue);
        WarmupRequest().GetAwaiter().GetResult();
    }

    /// <summary>Disposes the <see cref="ITurboHttpClient"/> and tears down the shared server.</summary>
    [GlobalCleanup]
    public override async Task GlobalCleanup()
    {
        await _clientHelper.DisposeAsync();
        await base.GlobalCleanup();
    }

    /// <inheritdoc />
    public override async Task WarmupRequest()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/benchmark/simple");
        using var response = await _clientHelper.Client.SendAsync(request, CancellationToken.None);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Issues <see cref="BenchmarkBaseClass.ConcurrencyLevel"/> concurrent GET requests to
    /// <c>/benchmark/simple</c> and waits for all to complete.
    /// </summary>
    [Benchmark]
    public Task ConcurrentRequests_Light()
    {
        var tasks = new Task[ConcurrencyLevel];
        for (var i = 0; i < ConcurrencyLevel; i++)
        {
            tasks[i] = SendLightRequest();
        }

        return Task.WhenAll(tasks);
    }

    /// <summary>
    /// Issues <see cref="BenchmarkBaseClass.ConcurrencyLevel"/> concurrent POST requests,
    /// each carrying a 10 KB body, and waits for all to complete.
    /// </summary>
    [Benchmark]
    public Task ConcurrentRequests_Heavy()
    {
        var tasks = new Task[ConcurrencyLevel];
        for (var i = 0; i < ConcurrencyLevel; i++)
        {
            tasks[i] = SendHeavyRequest();
        }

        return Task.WhenAll(tasks);
    }

    private async Task SendLightRequest()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/benchmark/simple");
        using var response = await _clientHelper.Client.SendAsync(request, CancellationToken.None);
        response.EnsureSuccessStatusCode();
    }

    private async Task SendHeavyRequest()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/benchmark/payload")
        {
            Content = new ByteArrayContent(HeavyPayload)
        };
        using var response = await _clientHelper.Client.SendAsync(request, CancellationToken.None);
        response.EnsureSuccessStatusCode();
    }
}
