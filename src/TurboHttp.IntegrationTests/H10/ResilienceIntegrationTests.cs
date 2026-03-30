using System.Net;
using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.H10;

[Collection("H10")]
public sealed class ResilienceIntegrationTests
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    public ResilienceIntegrationTests(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    private ClientHelper CreateClient() =>
        ClientHelper.CreateClient(_server.HttpPort, new Version(1, 0), system: _systemFixture.System);

    [Fact(DisplayName = "Resilience-H10-001: Content-Length mismatch causes exception or timeout")]
    public async Task ContentLength_Mismatch_Causes_Exception_Or_Timeout()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var helper = CreateClient();

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            var response = await helper.Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/resilience/content-length-mismatch"), cts.Token);
            await response.Content.ReadAsStringAsync(cts.Token);
        });
    }

    [Fact(DisplayName = "Resilience-H10-002: Corrupt gzip data causes graceful failure")]
    public async Task CorruptGzip_Causes_Graceful_Failure()
    {
        // ContentEncodingBidiStage catches decompression failures and passes the raw
        // response through unmodified — no exception, no hang, no crash.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var helper = CreateClient();

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/resilience/corrupt-gzip"), cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Body is readable (raw bytes passed through) — client does not crash.
        await response.Content.ReadAsByteArrayAsync(cts.Token);
        Assert.False(cts.IsCancellationRequested, "Client must not hang — CTS must not have fired.");
    }

    [Fact(DisplayName = "Resilience-H10-003: Corrupt brotli data causes graceful failure")]
    public async Task CorruptBrotli_Causes_Graceful_Failure()
    {
        // ContentEncodingBidiStage catches decompression failures and passes the raw
        // response through unmodified — no exception, no hang, no crash.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var helper = CreateClient();

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/resilience/corrupt-br"), cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Body is readable (raw bytes passed through) — client does not crash.
        await response.Content.ReadAsByteArrayAsync(cts.Token);
        Assert.False(cts.IsCancellationRequested, "Client must not hang — CTS must not have fired.");
    }

    [Fact(DisplayName = "Resilience-H10-004: Truncated body detected")]
    public async Task Truncated_Body_Detected()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var helper = CreateClient();

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            var response = await helper.Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/resilience/truncated-body/4"), cts.Token);
            await response.Content.ReadAsStringAsync(cts.Token);
        });
    }

    [Fact(DisplayName = "Resilience-H10-005: Slow headers within timeout succeed")]
    public async Task SlowHeaders_Within_Timeout_Succeed()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var helper = CreateClient();

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/resilience/slow-headers/500"), cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("slow-headers", body);
    }

    [Fact(DisplayName = "Resilience-H10-006: Slow body within timeout succeed")]
    public async Task SlowBody_Within_Timeout_Succeed()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var helper = CreateClient();

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/resilience/slow-body/500"), cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Contains("slow-body-first-half", body);
        Assert.Contains("slow-body-second-half", body);
    }

    [Fact(DisplayName = "Resilience-H10-007: Slow headers exceed timeout cause cancellation")]
    public async Task SlowHeaders_Exceed_Timeout_Cause_Cancellation()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var helper = CreateClient();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await helper.Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/resilience/slow-headers/10000"), cts.Token));
    }

    [Fact(DisplayName = "Resilience-H10-008: Empty response causes exception")]
    public async Task EmptyResponse_Causes_Exception()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var helper = CreateClient();

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await helper.Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/resilience/empty-response"), cts.Token));
    }
}
