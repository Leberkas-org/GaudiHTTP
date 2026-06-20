using System.Net;
using TurboHTTP.Client;
using TurboHTTP.IntegrationTests.Client.Shared;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.IntegrationTests.Client.H2;

/// <summary>
/// Repro for the HTTP/2 large-download hang in the 2026-06-19 benchmark run
/// (KestrelTurboDownloadBenchmarks [ConcurrencyLevel=1, DownloadBytes=8388608, HttpVersion=2.0] → NA,
/// "System.TimeoutException: The operation has timed out"). A SINGLE 8 MB response over one H2 stream
/// hung to the 120 s WaitAsync, while 1 MB over H2 — and 8 MB over H1.1 and H3 — all completed.
/// Suspected receive-path flow-control / WINDOW_UPDATE stall on a single large stream.
///
/// 1 MB is included first as a sanity check (it completes in the benchmark); the 8 MB download is the
/// configuration that hung.
/// </summary>
[Collection("H2")]
public sealed class LargeDownloadRegressionSpec : IntegrationSpecBase
{
    public LargeDownloadRegressionSpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture)
    {
    }

    // Build our own client below so we can pin a single H2 connection (one stream at a time),
    // matching the benchmark's ConcurrencyLevel=1 over the default pool.
    protected override ProtocolVariant? Variant => null;

    [Fact(Timeout = 180_000)]
    public async Task LargeDownload_should_complete_8MB_body_over_single_H2_stream()
    {
        await using var helper = CreateClient(
            new ProtocolVariant(TestHttpVersion.H2, tls: true),
            configureOptions: o => o.Http2.MaxConnectionsPerServer = 1);
        var client = helper.Client;

        // Warmup + iterations: the benchmark drained 8 MB ~13 times in sequence before it hung.
        await DownloadAsync(client, 1 * 1024 * 1024);
        for (var i = 0; i < 13; i++)
        {
            await DownloadAsync(client, 8 * 1024 * 1024);
        }
    }

    private async Task DownloadAsync(ITurboHttpClient client, int size)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            var response = await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, $"/bytes/{size}"), cts.Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // Drain exactly like the benchmark (Content.CopyToAsync(Stream.Null)).
            await response.Content.CopyToAsync(Stream.Null, cts.Token);
            response.Dispose();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !CancellationToken.IsCancellationRequested)
        {
            Assert.Fail(
                $"REPRO: a {size / (1024 * 1024)} MB HTTP/2 download did not complete within 30 s — " +
                "the receive path stalls on a large single stream (suspected missing/stuck WINDOW_UPDATE).");
        }
    }
}
