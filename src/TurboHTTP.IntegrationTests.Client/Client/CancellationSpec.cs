using System.Net;
using TurboHTTP.Client;
using TurboHTTP.IntegrationTests.Client.Shared;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.IntegrationTests.Client.Client;

[Collection("Cancellation")]
public sealed class CancellationSpec : IntegrationSpecBase
{
    public CancellationSpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture)
    {
    }

    private static readonly ProtocolVariant H2Tls = new(TestHttpVersion.H2, tls: true);
    private static readonly ProtocolVariant H11 = new(TestHttpVersion.H11, tls: false);

    [Fact(Timeout = 10000)]
    public async Task SendAsync_cancelled_by_user_should_throw_OperationCanceledException_h2()
    {
        await using var helper = CreateClient(H2Tls);
        var client = helper.Client;

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/delay/10"), cts.Token);
        });

        Assert.True(ex is OperationCanceledException or TaskCanceledException);
    }

    [Fact(Timeout = 10000)]
    public async Task SendAsync_cancelled_by_user_should_throw_OperationCanceledException_h11()
    {
        await using var helper = CreateClient(H11);
        var client = helper.Client;

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/delay/10"), cts.Token);
        });

        Assert.True(ex is OperationCanceledException or TaskCanceledException);
    }

    [Fact(Timeout = 15000)]
    public async Task SendAsync_cancelled_should_not_break_subsequent_requests_h2()
    {
        await using var helper = CreateClient(H2Tls);
        var client = helper.Client;

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/delay/10"), cts.Token);
        });

        var response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/get"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task SendAsync_cancelled_should_not_break_subsequent_requests_h11()
    {
        await using var helper = CreateClient(H11);
        var client = helper.Client;

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/delay/10"), cts.Token);
        });

        var response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/get"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task Timeout_should_cancel_and_allow_reuse_h2()
    {
        await using var helper = CreateClient(H2Tls, configureOptions: opts =>
        {
        });
        var client = helper.Client;
        client.Timeout = TimeSpan.FromMilliseconds(500);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/delay/10"), CancellationToken.None);
        });

        client.Timeout = TimeSpan.FromMinutes(5);

        var response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/get"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task Timeout_should_cancel_and_allow_reuse_h11()
    {
        await using var helper = CreateClient(H11);
        var client = helper.Client;
        client.Timeout = TimeSpan.FromMilliseconds(500);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/delay/10"), CancellationToken.None);
        });

        client.Timeout = TimeSpan.FromMinutes(5);

        var response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/get"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task CancelPendingRequests_should_cancel_inflight_and_allow_reuse_h2()
    {
        await using var helper = CreateClient(H2Tls);
        var client = helper.Client;

        var slowTask = client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/delay/10"), CancellationToken);

        await Task.Delay(200, CancellationToken);
        client.CancelPendingRequests();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await slowTask;
        });

        var response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/get"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task CancelPendingRequests_should_cancel_inflight_and_allow_reuse_h11()
    {
        await using var helper = CreateClient(H11);
        var client = helper.Client;

        var slowTask = client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/delay/10"), CancellationToken);

        await Task.Delay(200, CancellationToken);
        client.CancelPendingRequests();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await slowTask;
        });

        var response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/get"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task Channel_path_with_timeout_should_cancel_and_allow_reuse()
    {
        await using var helper = CreateClient(H2Tls);
        var client = helper.Client;

        var request = new HttpRequestMessage(HttpMethod.Get, "/delay/10")
            .WithTimeout(TimeSpan.FromMilliseconds(500));

        var responseTask = request.GetResponseAsync(CancellationToken);
        await client.Requests.WriteAsync(request, CancellationToken);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await responseTask;
        });

        var fast = new HttpRequestMessage(HttpMethod.Get, "/get");
        var fastResponse = await client.SendAsync(fast, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, fastResponse.StatusCode);
    }
}
