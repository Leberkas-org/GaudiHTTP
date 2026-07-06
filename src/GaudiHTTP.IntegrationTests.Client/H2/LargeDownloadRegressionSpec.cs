using System.Net;
using GaudiHTTP.Client;
using GaudiHTTP.IntegrationTests.Client.Shared;
using GaudiHTTP.Tests.Shared;
using Servus.Diagnostics;

namespace GaudiHTTP.IntegrationTests.Client.H2;

/// <summary>
/// Repro for the HTTP/2 large-download hang in the 2026-06-19 benchmark run
/// (GaudiClientDownloadBenchmarks [ConcurrencyLevel=1, DownloadBytes=8388608, HttpVersion=2.0] → NA,
/// "System.TimeoutException: The operation has timed out"). A SINGLE 8 MB response over one H2 stream
/// hung to the 120 s WaitAsync, while 1 MB over H2 — and 8 MB over H1.1 and H3 — all completed.
/// Root cause (2026-07): shared-pool buffer corruption — a double dispose returned another owner's
/// array to the process-wide pool, desyncing the H2 frame decoder mid-download. Flow control /
/// WINDOW_UPDATE was ruled out.
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

    // Stall post-mortem: capture per-frame Protocol traces (DATA in/out, WINDOW_UPDATE in/out,
    // pause/resume) in a ring buffer; DownloadAsync dumps it to a file when the 30 s budget trips.
    private static readonly RingBufferTraceListener TraceRing =
        new(capacity: 128 * 1024, category => category is "Protocol" or "Stage" or "Transport");

    [Fact(Timeout = 180_000)]
    public async Task LargeDownload_should_complete_8MB_body_over_single_H2_stream()
    {
        // Additive registration: Tracing.Configure would REPLACE the logger bridge installed by
        // ActorSystemFixture and swallow every other test's warnings while this spec runs.
        using var traceScope = TestTracing.Root.Add(TraceRing);
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

    private async Task DownloadAsync(IGaudiHttpClient client, int size)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            var response = await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, $"/bytes/{size}"), cts.Token);

            // This guard needs a server that streams an arbitrary-size body. The Kestrel backend's
            // /bytes/{n} does; the Docker (httpbin) backend caps the size and rejects it up front with 400
            // (some servers use 413). Skip ONLY on those size-rejection statuses — any other non-200
            // (404, 5xx, ...) is a real failure and must not be masked. The stall this guards against
            // surfaces as the 30 s timeout below, never as a status code.
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge)
            {
                response.Dispose();
                Assert.Skip(
                    $"Backend rejected /bytes/{size} with {(int)response.StatusCode} (size cap); "
                    + "run with the Kestrel backend to exercise the H2 receive flow-control fix.");
                return;
            }

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // Drain exactly like the benchmark (Content.CopyToAsync(Stream.Null)).
            await response.Content.CopyToAsync(Stream.Null, cts.Token);
            response.Dispose();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !CancellationToken.IsCancellationRequested)
        {
            var dumpPath = Path.Combine(Path.GetTempPath(),
                $"h2-large-download-stall-{DateTime.UtcNow:yyyyMMdd_HHmmss}.trace.log");
            await File.WriteAllTextAsync(dumpPath, TraceRing.Dump(), CancellationToken.None);

            Assert.Fail(
                $"REPRO: a {size / (1024 * 1024)} MB HTTP/2 download did not complete within 30 s. " +
                "Historically caused by shared-pool buffer corruption desyncing the frame decoder " +
                "(flow control / WINDOW_UPDATE was ruled out) — analyze the dump, don't guess. " +
                $"Protocol trace ring dumped to: {dumpPath}");
        }
        catch (Exception ex) when (ex is not Xunit.Sdk.SkipException)
        {
            // Receive-path failures now fail fast (the body reader faults on connection loss instead
            // of stalling to the 30 s budget above), so the post-mortem ring must also be dumped for
            // this path — otherwise the fast failure hides exactly the frame trace the stall dump
            // was added to capture. The original exception propagates unchanged.
            var dumpPath = Path.Combine(Path.GetTempPath(),
                $"h2-large-download-fault-{DateTime.UtcNow:yyyyMMdd_HHmmss}.trace.log");
            await File.WriteAllTextAsync(dumpPath, TraceRing.Dump(), CancellationToken.None);
            await Console.Error.WriteLineAsync(
                $"[LargeDownloadRegressionSpec] {ex.GetType().Name}: {ex.Message} — protocol trace ring dumped to: {dumpPath}");
            throw;
        }
    }
}
