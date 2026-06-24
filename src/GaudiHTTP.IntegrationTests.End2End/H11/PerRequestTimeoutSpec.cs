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

    protected override TimeSpan ClientTimeout => TimeSpan.FromSeconds(30);

    protected override void ConfigureEndpoints(WebApplication app)
    {
        app.MapGet("/ping", () => Results.Ok("pong"));

        app.MapGet("/slow", async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            return Results.Ok("slow");
        });
    }

    [Fact(Timeout = 30000)]
    public async Task PerRequestTimeout_should_cancel_slow_request_before_global_timeout()
    {
        await WarmupAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/slow")
            .WithTimeout(TimeSpan.FromMilliseconds(500));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => Client.SendAsync(request, CancellationToken));
    }

    [Fact(Timeout = 30000)]
    public async Task PerRequestTimeout_should_not_affect_request_without_it()
    {
        await WarmupAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/slow");

        var response = await Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 45000)]
    public async Task PerRequestTimeout_should_not_leak_to_subsequent_request()
    {
        await WarmupAsync();

        var timed = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/slow")
            .WithTimeout(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => Client.SendAsync(timed, CancellationToken));

        var plain = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/slow");
        var response = await Client.SendAsync(plain, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task WarmupAsync()
    {
        var warmup = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/ping");
        var response = await Client.SendAsync(warmup, CancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
