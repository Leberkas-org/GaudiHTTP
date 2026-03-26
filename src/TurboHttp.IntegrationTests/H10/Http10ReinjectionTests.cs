using System.Diagnostics;
using System.Net;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.Protocol.RFC9110;
using TurboHttp.Protocol.RFC9111;
namespace TurboHttp.IntegrationTests.H10;

/// <summary>
/// Integration tests that reproduce the original HTTP/1.0 deadlock scenarios caused by
/// feature BidiStage re-injection (retry, cache revalidation, compression negotiation).
/// Each test runs 100 iterations to verify no deadlock occurs under sustained load.
/// The pending-work signaling mechanism prevents premature substream completion in
/// GroupByHostKeyStage while re-injections are in-flight.
/// </summary>
[Collection("H10")]
public sealed class Http10ReinjectionTests
{
    private const int Iterations = 100;

    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;
    private readonly ITestOutputHelper _output;

    public Http10ReinjectionTests(ServerFixture server, ActorSystemFixture systemFixture, ITestOutputHelper output)
    {
        _server = server;
        _systemFixture = systemFixture;
        _output = output;
    }

    // ── Test 1: Retry after 503 — succeed-after-2 triggers retry re-injection ──

    [Fact(Timeout = 60000, DisplayName = "Reinjection-H10-001: Retry after 503 does not deadlock over 100 iterations")]
    public async Task Retry_After_503_No_Deadlock()
    {
        var sw = Stopwatch.StartNew();

        for (var i = 0; i < Iterations; i++)
        {
            await using var helper = ClientHelper.CreateClient(
                _server.HttpPort,
                new Version(1, 0),
                configure: builder => builder.WithRetry(new RetryPolicy { MaxRetries = 3 }),
                system: _systemFixture.System);

            var key = Guid.NewGuid().ToString("N");
            var request = new HttpRequestMessage(HttpMethod.Get, $"/retry/succeed-after/2?key={key}");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var response = await helper.Client.SendAsync(request, cts.Token);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(cts.Token);
            Assert.Equal("success", body);

            if ((i + 1) % 25 == 0)
            {
                _output.WriteLine(
                    $"[Retry] iteration {i + 1}/{Iterations} complete — elapsed {sw.ElapsedMilliseconds}ms");
            }
        }

        _output.WriteLine($"[Retry] all {Iterations} iterations passed in {sw.ElapsedMilliseconds}ms");
    }

    // ── Test 2: Cache revalidation — must-revalidate triggers If-None-Match → 304 ──

    [Fact(Timeout = 60000, DisplayName = "Reinjection-H10-002: Cache revalidation does not deadlock over 100 iterations")]
    public async Task Cache_Revalidation_No_Deadlock()
    {
        var sw = Stopwatch.StartNew();

        for (var i = 0; i < Iterations; i++)
        {
            var store = new CacheStore(CachePolicy.Default);

            await using var helper = ClientHelper.CreateClient(
                _server.HttpPort,
                new Version(1, 0),
                configure: builder => builder.WithCache(store),
                system: _systemFixture.System);

            // First request: populates the cache with ETag + max-age=0, must-revalidate
            var request1 = new HttpRequestMessage(HttpMethod.Get, "/cache/must-revalidate");
            using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var response1 = await helper.Client.SendAsync(request1, cts1.Token);
            Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
            var body1 = await response1.Content.ReadAsStringAsync(cts1.Token);
            Assert.False(string.IsNullOrEmpty(body1), "First response body should be non-empty");

            // Second request: stale cache entry → revalidation with If-None-Match → 304 → merge
            // This is the re-injection path that previously caused deadlock
            var request2 = new HttpRequestMessage(HttpMethod.Get, "/cache/must-revalidate");
            using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var response2 = await helper.Client.SendAsync(request2, cts2.Token);
            Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
            var body2 = await response2.Content.ReadAsStringAsync(cts2.Token);

            // After 304 merge, the body should match the original cached body
            Assert.Equal(body1, body2);

            if ((i + 1) % 25 == 0)
            {
                _output.WriteLine(
                    $"[Cache] iteration {i + 1}/{Iterations} complete — elapsed {sw.ElapsedMilliseconds}ms");
            }
        }

        _output.WriteLine($"[Cache] all {Iterations} iterations passed in {sw.ElapsedMilliseconds}ms");
    }

    // ── Test 3: Compression negotiation — decompressed response over 100 iterations ──

    [Fact(Timeout = 60000, DisplayName = "Reinjection-H10-003: Compression negotiation does not deadlock over 100 iterations")]
    public async Task Compression_Negotiation_No_Deadlock()
    {
        var sw = Stopwatch.StartNew();

        for (var i = 0; i < Iterations; i++)
        {
            await using var helper = ClientHelper.CreateClient(
                _server.HttpPort,
                new Version(1, 0),
                configure: builder => builder.WithDecompression(),
                system: _systemFixture.System);

            // Alternate between gzip and brotli to exercise different decompression paths
            var encoding = i % 2 == 0 ? "gzip" : "br";
            var request = new HttpRequestMessage(HttpMethod.Get, "/compress/negotiate");
            request.Headers.TryAddWithoutValidation("Accept-Encoding", encoding);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var response = await helper.Client.SendAsync(request, cts.Token);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsByteArrayAsync(cts.Token);

            // Negotiate endpoint produces 1 KB payload
            Assert.Equal(1024, body.Length);

            // Verify decompressed content matches expected repeating ASCII pattern
            for (var j = 0; j < body.Length; j++)
            {
                Assert.Equal((byte)('A' + j % 26), body[j]);
            }

            if ((i + 1) % 25 == 0)
            {
                _output.WriteLine(
                    $"[Compression] iteration {i + 1}/{Iterations} complete — elapsed {sw.ElapsedMilliseconds}ms");
            }
        }

        _output.WriteLine($"[Compression] all {Iterations} iterations passed in {sw.ElapsedMilliseconds}ms");
    }

    // ── Test 4: Mixed features — retry, cache, and compression in sequence ──

    [Fact(Timeout = 60000, DisplayName = "Reinjection-H10-004: Mixed features pipeline stays alive across retry, cache, and compression")]
    public async Task Mixed_Features_Pipeline_Stays_Alive()
    {
        var sw = Stopwatch.StartNew();

        for (var i = 0; i < Iterations; i++)
        {
            var store = new CacheStore(CachePolicy.Default);

            await using var helper = ClientHelper.CreateClient(
                _server.HttpPort,
                new Version(1, 0),
                configure: builder => builder
                    .WithRetry(new RetryPolicy { MaxRetries = 3 })
                    .WithCache(store)
                    .WithDecompression(),
                system: _systemFixture.System);

            // Step 1: Retry — 503 on first attempt, 200 on second
            var retryKey = Guid.NewGuid().ToString("N");
            var retryReq = new HttpRequestMessage(HttpMethod.Get, $"/retry/succeed-after/2?key={retryKey}");

            using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var retryResp = await helper.Client.SendAsync(retryReq, cts1.Token);
            Assert.Equal(HttpStatusCode.OK, retryResp.StatusCode);
            var retryBody = await retryResp.Content.ReadAsStringAsync(cts1.Token);
            Assert.Equal("success", retryBody);

            // Step 2: Cache revalidation — populate then revalidate
            var cacheReq1 = new HttpRequestMessage(HttpMethod.Get, "/cache/must-revalidate");
            using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var cacheResp1 = await helper.Client.SendAsync(cacheReq1, cts2.Token);
            Assert.Equal(HttpStatusCode.OK, cacheResp1.StatusCode);

            var cacheReq2 = new HttpRequestMessage(HttpMethod.Get, "/cache/must-revalidate");
            using var cts3 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var cacheResp2 = await helper.Client.SendAsync(cacheReq2, cts3.Token);
            Assert.Equal(HttpStatusCode.OK, cacheResp2.StatusCode);

            // Step 3: Compression — gzip decompression
            var compReq = new HttpRequestMessage(HttpMethod.Get, "/compress/gzip/1");
            using var cts4 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var compResp = await helper.Client.SendAsync(compReq, cts4.Token);
            Assert.Equal(HttpStatusCode.OK, compResp.StatusCode);
            var compBody = await compResp.Content.ReadAsByteArrayAsync(cts4.Token);
            Assert.Equal(1024, compBody.Length);

            if ((i + 1) % 25 == 0)
            {
                _output.WriteLine(
                    $"[Mixed] iteration {i + 1}/{Iterations} complete — elapsed {sw.ElapsedMilliseconds}ms");
            }
        }

        _output.WriteLine($"[Mixed] all {Iterations} iterations passed in {sw.ElapsedMilliseconds}ms");
    }
}
