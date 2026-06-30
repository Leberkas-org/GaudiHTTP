using BenchmarkDotNet.Attributes;
using GaudiHTTP.Benchmarks.Internal;

namespace GaudiHTTP.Benchmarks.Client.Streaming;

/// <summary>
/// Steady-state pipeline throughput for the GaudiHttp channel API: requests are written to
/// <see cref="GaudiHTTP.Client.IGaudiHttpClient.Requests"/> continuously while responses drain from
/// <see cref="GaudiHTTP.Client.IGaudiHttpClient.Responses"/>, holding a fixed in-flight window. Unlike
/// the fire-all/drain-all streaming benchmark, this measures the intended sustained usage.
/// </summary>
[WarmupCount(3)]
[IterationCount(10)]
public class SustainedPipelineGaudiBenchmarks : KestrelBaseClass
{
    public enum Workload { Light, Slow }

    private const int TotalRequests = 2000;

    [Params(64, 256, 1024)]
    public int InFlightWindow { get; set; }

    [Params(Workload.Light, Workload.Slow)]
    public Workload Mode { get; set; }

    private ClientHelper _client = null!;
    private Uri _uri = null!;

    [GlobalSetup]
    public override async Task GlobalSetup()
    {
        await base.GlobalSetup();
        _client = ClientHelper.CreateStreamingClient(BaseAddress, HttpVersionValue);
        _uri = Mode == Workload.Slow ? SlowUri(2) : LightUri;
        await WarmupRequest();
    }

    [GlobalCleanup]
    public override async Task GlobalCleanup()
    {
        await _client.DisposeAsync();
        await base.GlobalCleanup();
    }

    public override async Task WarmupRequest()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _uri);
        using var response = await _client.Client.SendAsync(request, CancellationToken.None);
        response.EnsureSuccessStatusCode();
    }

    [Benchmark(OperationsPerInvoke = TotalRequests)]
    public async Task SustainedPipeline()
    {
        var client = _client.Client;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var ct = cts.Token;
        using var window = new SemaphoreSlim(InFlightWindow, InFlightWindow);

        while (client.Responses.TryRead(out var stale))
        {
            stale.Dispose();
        }

        var writer = Task.Run(async () =>
        {
            for (var i = 0; i < TotalRequests; i++)
            {
                await window.WaitAsync(ct);
                await client.Requests.WriteAsync(new HttpRequestMessage(HttpMethod.Get, _uri), ct);
            }
        }, ct);

        var received = 0;
        while (received < TotalRequests)
        {
            if (!await client.Responses.WaitToReadAsync(ct))
            {
                break;
            }

            while (client.Responses.TryRead(out var response))
            {
                await response.Content.CopyToAsync(Stream.Null, ct);
                response.Dispose();
                window.Release();
                if (++received >= TotalRequests)
                {
                    break;
                }
            }
        }

        await writer.WaitAsync(ct);
    }
}
