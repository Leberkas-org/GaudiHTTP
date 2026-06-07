using TurboHTTP.Client;
using BenchmarkDotNet.Attributes;
using TurboHTTP.Benchmarks.Internal;

namespace TurboHTTP.Benchmarks.Binkraken;

[MemoryDiagnoser]
[WarmupCount(3)]
[IterationCount(10)]
public class BinkrakenTurboStreamingConcurrentBenchmarks : BinkrakenBaseClass
{
    [Params(1, 512, 4096)]
    public int ConcurrencyLevel { get; set; }

    private static readonly Uri BaseAddress = new("https://binkraken.com");

    private ClientHelper _clientHelper = null!;

    [GlobalSetup]
    public override async Task GlobalSetup()
    {
        await base.GlobalSetup();
        _clientHelper = ClientHelper.CreateStreamingClient(BaseAddress, HttpVersionValue);
        await WarmupRequest();
    }

    [GlobalCleanup]
    public override async Task GlobalCleanup()
    {
        await _clientHelper.DisposeAsync();
        await base.GlobalCleanup();
    }

    [IterationCleanup]
    public void DrainResponses()
    {
        try
        {
            while (_clientHelper.Client.Responses.TryRead(out var stale))
            {
                stale.Dispose();
            }
        }
        catch
        {
            // Channel may be in a faulted state — ignore during cleanup.
        }
    }

    public override async Task WarmupRequest()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var request = new HttpRequestMessage(HttpMethod.Get, LightUri);
        using var response = await _clientHelper.Client.SendAsync(request, cts.Token);
        response.EnsureSuccessStatusCode();
    }

    [Benchmark]
    public async Task ConcurrentRequests_Light()
    {
        await StreamRequests(LightUri);
    }

    [Benchmark]
    public async Task ConcurrentRequests_Heavy()
    {
        await StreamRequests(HeavyUri);
    }

    private async Task StreamRequests(Uri uri)
    {
        var client = _clientHelper.Client;
        var count = ConcurrencyLevel;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = cts.Token;

        try
        {
            var writer = Task.Run(async () =>
            {
                for (var i = 0; i < count; i++)
                {
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
                    await response.Content.ReadAsByteArrayAsync(ct);
                    response.Dispose();
                    received++;
                    if (received >= count)
                    {
                        break;
                    }
                }
            }

            await writer.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
