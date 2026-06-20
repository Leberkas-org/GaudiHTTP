using System.Net;
using TurboHTTP.IntegrationTests.Client.Shared;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.IntegrationTests.Client.H11;

/// <summary>
/// Repro for the single-connection HTTP/1.1 concurrency deadlock in the 2026-06-19 benchmark run
/// (KestrelTurboSingleConnectionBenchmarks [ConcurrencyLevel=64 and 256, HttpVersion=1.1] → NA).
/// With MaxConnectionsPerServer forced to 1, the benchmark completed a few iterations of N concurrent
/// GETs (~1.5 ms each) and then HUNG to the 60 s WaitAsync — an intermittent pipelining/dispatch
/// deadlock when many requests share one H1.1 connection. The H2 and H3 single-connection variants
/// produced results; only H1.1 went NA.
///
/// The deadlock is intermittent, so the spec drives many rounds of concurrent bursts on the single
/// connection and fails the first round that does not drain within a generous per-round budget.
///
/// ROOT CAUSE (2026-06-20, fixed): under heavy pipelining the server streams many responses back to
/// back, so a response's status line or header block is frequently split across two TCP reads. The
/// H1.1 client decoder kept no cross-read remainder, so the unconsumed prefix of a split header was
/// discarded and the next read's continuation parsed as garbage ("Malformed header field"), faulting
/// that request and stranding its in-flight pipelined siblings. Fixed by retaining the unconsumed
/// prefix in Http11ClientStateMachine (_partialResponse) and prepending it to the next read; the
/// deterministic repro lives in Http11ClientFragmentedResponseSpec. This stress guard now drains 256 ×
/// 40 cleanly; it failed ~50-80% of runs before the fix.
/// </summary>
[Collection("H11")]
public sealed class SingleConnectionConcurrencyRegressionSpec : IntegrationSpecBase
{
    public SingleConnectionConcurrencyRegressionSpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture)
    {
    }

    // Build our own single-connection client below; do not use the default multi-connection Client.
    protected override ProtocolVariant? Variant => null;

    [Fact(Timeout = 180_000)]
    public async Task SingleConnection_should_not_deadlock_under_concurrent_H11_requests()
    {
        await using var helper = CreateClient(
            new ProtocolVariant(TestHttpVersion.H11, tls: false),
            configureOptions: o => o.Http1.MaxConnectionsPerServer = 1);
        var client = helper.Client;

        // 256 concurrency matches the heavier of the two NA configs ([256, 1.1]); many rounds give the
        // intermittent single-connection deadlock repeated chances to surface.
        const int concurrency = 256;
        const int rounds = 40;

        for (var round = 0; round < rounds; round++)
        {
            var tasks = new Task<HttpResponseMessage>[concurrency];
            for (var i = 0; i < concurrency; i++)
            {
                tasks[i] = client.SendAsync(
                    new HttpRequestMessage(HttpMethod.Get, "/get"), CancellationToken);
            }

            try
            {
                var responses = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(15), CancellationToken);
                Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
                foreach (var r in responses)
                {
                    r.Dispose();
                }
            }
            catch (TimeoutException)
            {
                Assert.Fail(
                    $"REPRO: round {round} of {concurrency} concurrent HTTP/1.1 GETs on a single connection " +
                    "did not complete within 15 s — single-connection request dispatch deadlocked.");
            }
        }
    }
}
