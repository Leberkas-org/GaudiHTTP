using TurboHTTP.Client;
using BenchmarkDotNet.Attributes;
using TurboHTTP.Benchmarks.Internal;

namespace TurboHTTP.Benchmarks.Kestrel;

[MemoryDiagnoser]
[WarmupCount(3)]
[IterationCount(10)]
public class KestrelTurboStreamingConcurrentBenchmarks : KestrelBaseClass
{
    [Params(1, 512, 4096)] public int ConcurrencyLevel { get; set; }

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
        await StreamRequests(LightUri, HttpMethod.Get);
    }

    [Benchmark]
    public async Task ConcurrentRequests_Heavy()
    {
        await StreamRequests(UploadUri, HttpMethod.Post);
    }

    private async Task StreamRequests(Uri uri, HttpMethod method)
    {
        var client = _clientHelper.Client;
        var count = ConcurrencyLevel;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = cts.Token;

        // Drain stale responses from prior iterations before starting
        while (client.Responses.TryRead(out var stale))
        {
            stale.Dispose();
        }

        // Cap in-flight requests to avoid unbounded memory growth at high CL.
        // 512 matches Kestrel's MaxStreamsPerConnection — more just queues.
        using var throttle = new SemaphoreSlim(Math.Min(count, 512));

        try
        {
            var writer = Task.Run(async () =>
            {
                for (var i = 0; i < count; i++)
                {
                    await throttle.WaitAsync(ct);
                    var request = new HttpRequestMessage(method, uri);
                    if (method == HttpMethod.Post)
                    {
                        request.Content = new ByteArrayContent(HeavyPayload);
                    }

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
                    throttle.Release();
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
