using BenchmarkDotNet.Attributes;
using TurboHTTP.Benchmarks.Internal;

namespace TurboHTTP.Benchmarks.Kestrel;

/// <summary>
/// Benchmarks measuring <see cref="ITurboHttpClient"/> performance using the channel-based
/// streaming API for a single sequential request against a localhost Kestrel server.
/// </summary>
[MemoryDiagnoser]
[WarmupCount(3)]
[IterationCount(10)]
public class KestrelTurboStreamingSingleBenchmarks : KestrelBaseClass
{
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

    /// <inheritdoc />
    public override async Task WarmupRequest()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LightUri);
        using var response = await _clientHelper.Client.SendAsync(request, CancellationToken.None);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>GET to <c>/benchmark/simple</c> (~3 B response).</summary>
    [Benchmark]
    public async Task SingleRequest_Light()
    {
        var client = _clientHelper.Client;
        var request = new HttpRequestMessage(HttpMethod.Get, LightUri);

        await client.Requests.WriteAsync(request);

        await client.Responses.WaitToReadAsync();
        if (client.Responses.TryRead(out var response))
        {
            response.Dispose();
        }
    }

    /// <summary>POST to <c>/benchmark/payload</c> with 10 KB body.</summary>
    [Benchmark]
    public async Task SingleRequest_Heavy()
    {
        var client = _clientHelper.Client;
        var request = new HttpRequestMessage(HttpMethod.Post, HeavyUri)
        {
            Content = new ByteArrayContent(HeavyPayload)
        };

        await client.Requests.WriteAsync(request);

        await client.Responses.WaitToReadAsync();
        if (client.Responses.TryRead(out var response))
        {
            response.Dispose();
        }
    }
}
