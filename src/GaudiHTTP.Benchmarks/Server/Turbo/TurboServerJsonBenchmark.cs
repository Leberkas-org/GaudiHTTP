using BenchmarkDotNet.Attributes;
using TurboHTTP.Benchmarks.Internal;

namespace TurboHTTP.Benchmarks.Server.Turbo;

[MemoryDiagnoser]
[WarmupCount(3)]
[IterationCount(10)]
public class TurboServerJsonBenchmark : TurboServerBaseClass
{
    private const int MaxFanOut = 1024;

    [Params(1, 64, 256)]
    public int ConcurrencyLevel { get; set; }

    private HttpClient _httpClient = null!;
    private Task[] _tasks = null!;
    private SemaphoreSlim _fanOutGate = null!;

    [GlobalSetup]
    public override async Task GlobalSetup()
    {
        await base.GlobalSetup();

        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            EnableMultipleHttp2Connections = true,
            MaxConnectionsPerServer = 128,
            SslOptions = { RemoteCertificateValidationCallback = (_, _, _, _) => true },
        };

        _httpClient = new HttpClient(handler)
        {
            DefaultRequestVersion = HttpVersionValue,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Timeout = TimeSpan.FromSeconds(30),
        };

        _tasks = new Task[ConcurrencyLevel];
        _fanOutGate = new SemaphoreSlim(MaxFanOut, MaxFanOut);
        await WarmupRequest();
    }

    [GlobalCleanup]
    public override async Task GlobalCleanup()
    {
        _fanOutGate.Dispose();
        _httpClient.Dispose();
        await base.GlobalCleanup();
    }

    public override async Task WarmupRequest()
    {
        using var response = await _httpClient.GetAsync(JsonUri);
        response.EnsureSuccessStatusCode();
    }

    [Benchmark]
    public async Task Json_Sequential()
    {
        using var response = await _httpClient.GetAsync(JsonUri);
        response.EnsureSuccessStatusCode();
    }

    [Benchmark]
    [BenchmarkCategory("Concurrent")]
    public Task Json_Concurrent()
    {
        for (var i = 0; i < ConcurrencyLevel; i++)
        {
            _tasks[i] = SendRequest();
        }
        return Task.WhenAll(_tasks).WaitAsync(TimeSpan.FromSeconds(30));
    }

    private async Task SendRequest()
    {
        await _fanOutGate.WaitAsync();
        try
        {
            using var response = await _httpClient.GetAsync(JsonUri);
            response.EnsureSuccessStatusCode();
        }
        finally
        {
            _fanOutGate.Release();
        }
    }
}
