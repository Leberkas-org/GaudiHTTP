using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using TurboHTTP.Client;
using TurboHTTP.IntegrationTests.End2End.Shared;

namespace TurboHTTP.IntegrationTests.End2End.H11;

[Collection("H11")]
public sealed class PerRequestTimeoutSpec : End2EndSpecBase
{
    protected override Version ProtocolVersion => HttpVersion.Version11;

    protected override void ConfigureEndpoints(WebApplication app)
    {
        // Responds only after 3s — far below the 10s global client timeout the base sets,
        // so without a per-request timeout this request SUCCEEDS.
        app.MapGet("/slow", async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            return Results.Ok("slow");
        });
    }

    [Fact(Timeout = 15000)]
    public async Task PerRequestTimeout_should_cancel_slow_request_before_global_timeout()
    {
        // Global timeout is 10s (set by the base). The 3s endpoint would otherwise succeed.
        // A 500ms per-request timeout must cancel it first — fails if WithTimeout is ignored.
        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/slow")
            .WithTimeout(TimeSpan.FromMilliseconds(500));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => Client.SendAsync(request, CancellationToken));
    }

    [Fact(Timeout = 15000)]
    public async Task PerRequestTimeout_should_not_affect_request_without_it()
    {
        // Same 3s endpoint, no per-request timeout → the 10s global timeout lets it complete.
        // Proves the cancellation above is caused by the per-request value, not the endpoint itself.
        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/slow");

        var response = await Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task PerRequestTimeout_should_not_leak_to_subsequent_request()
    {
        // A short per-request timeout must not stick to the client: the next request
        // without one falls back to the 10s global timeout and completes.
        var timed = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/slow")
            .WithTimeout(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => Client.SendAsync(timed, CancellationToken));

        var plain = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/slow");
        var response = await Client.SendAsync(plain, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
