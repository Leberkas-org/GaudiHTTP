using BenchmarkDotNet.Attributes;
using GaudiHTTP.Benchmarks.Internal;
using GaudiHTTP.Client;

namespace GaudiHTTP.Benchmarks.Client.ColdStart;

/// <summary>
/// Cold-start cost for <see cref="IGaudiHttpClient"/>: each measured invocation creates a fresh
/// client (ActorSystem + pipeline materialization) and performs its FIRST request against an
/// already-warm Kestrel server, capturing connection establishment (DNS/TCP/TLS/QUIC handshake) and
/// actor-system spin-up. <see cref="InvocationCountAttribute"/> = 1 keeps each measurement a genuine
/// first request; created clients are stashed and disposed in cleanup so teardown never enters the
/// measured region. NOTE: holds several ActorSystems alive per parameter combination — cold-start is
/// inherently noisier than the steady-state suites; read it as an order-of-magnitude signal.
/// </summary>
[WarmupCount(2)]
[IterationCount(8)]
[InvocationCount(1, 1)]
public class GaudiClientColdStartBenchmarks : KestrelBaseClass
{
    private readonly List<ClientHelper> _created = new();

    [GlobalSetup]
    public override Task GlobalSetup() => base.GlobalSetup();

    [GlobalCleanup]
    public override async Task GlobalCleanup()
    {
        foreach (var helper in _created)
        {
            await helper.DisposeAsync();
        }

        _created.Clear();
        await base.GlobalCleanup();
    }

    [Benchmark]
    public async Task ColdStartFirstRequest()
    {
        var helper = ClientHelper.CreateClient(BaseAddress, HttpVersionValue);
        _created.Add(helper);

        using var request = new HttpRequestMessage(HttpMethod.Get, LightUri);
        using var response = await helper.Client.SendAsync(request, CancellationToken.None);
        response.EnsureSuccessStatusCode();
    }
}
