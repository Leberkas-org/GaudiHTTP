using System.Net;
using TurboHTTP.Client;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Stages.Client;

namespace TurboHTTP.Tests.Streams.Stages.Client;

public sealed class RequestEnricherTimeoutSpec
{
    private static TurboRequestOptions CreateOptions(TimeSpan timeout)
    {
        var msg = new HttpRequestMessage();
        return new TurboRequestOptions(
            BaseAddress: new Uri("https://example.com"),
            DefaultRequestHeaders: msg.Headers,
            DefaultRequestVersion: HttpVersion.Version11,
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrLower,
            Timeout: timeout,
            Credentials: null,
            PreAuthenticate: false
        );
    }

    [Fact(Timeout = 5000)]
    public void Enrich_should_set_cancellation_token_from_default_timeout()
    {
        var options = CreateOptions(TimeSpan.FromSeconds(5));
        var enricher = new RequestEnricher(() => options);
        var request = new HttpRequestMessage(HttpMethod.Get, "/test");

        enricher.Enrich(request);

        Assert.True(request.Options.TryGetValue(OptionsKey.CancellationTokenKey, out var ct));
        Assert.True(ct.CanBeCanceled);
        Assert.False(ct.IsCancellationRequested);
    }

    [Fact(Timeout = 5000)]
    public void Enrich_should_not_set_cancellation_token_when_timeout_is_infinite()
    {
        var options = CreateOptions(System.Threading.Timeout.InfiniteTimeSpan);
        var enricher = new RequestEnricher(() => options);
        var request = new HttpRequestMessage(HttpMethod.Get, "/test");

        enricher.Enrich(request);

        Assert.False(request.Options.TryGetValue(OptionsKey.CancellationTokenKey, out _));
    }

    [Fact(Timeout = 5000)]
    public void Enrich_should_not_overwrite_existing_cancellation_token()
    {
        var options = CreateOptions(TimeSpan.FromSeconds(5));
        var enricher = new RequestEnricher(() => options);
        var request = new HttpRequestMessage(HttpMethod.Get, "/test");

        using var userCts = new CancellationTokenSource();
        request.SetCancellationToken(userCts.Token);

        enricher.Enrich(request);

        Assert.True(request.Options.TryGetValue(OptionsKey.CancellationTokenKey, out var ct));
        Assert.Equal(userCts.Token, ct);
    }

    [Fact(Timeout = 5000)]
    public void Enrich_should_use_per_request_timeout_over_default()
    {
        var options = CreateOptions(TimeSpan.FromSeconds(30));
        var enricher = new RequestEnricher(() => options);
        var request = new HttpRequestMessage(HttpMethod.Get, "/test")
            .WithTimeout(TimeSpan.FromMilliseconds(100));

        enricher.Enrich(request);

        Assert.True(request.Options.TryGetValue(OptionsKey.CancellationTokenKey, out var ct));
        Assert.True(ct.CanBeCanceled);
    }

    [Fact(Timeout = 5000)]
    public async Task Enrich_timeout_should_fire_cancellation_token()
    {
        var options = CreateOptions(TimeSpan.FromMilliseconds(50));
        var enricher = new RequestEnricher(() => options);
        var request = new HttpRequestMessage(HttpMethod.Get, "/test");

        enricher.Enrich(request);

        Assert.True(request.Options.TryGetValue(OptionsKey.CancellationTokenKey, out var ct));
        await Task.Delay(200, TestContext.Current.CancellationToken);
        Assert.True(ct.IsCancellationRequested);
    }

    [Fact(Timeout = 5000)]
    public async Task Enrich_should_cancel_pending_request_on_timeout()
    {
        var options = CreateOptions(TimeSpan.FromMilliseconds(50));
        var enricher = new RequestEnricher(() => options);
        var request = new HttpRequestMessage(HttpMethod.Get, "/test");

        var pending = PendingRequest.Rent();
        request.Options.Set(OptionsKey.Key, pending);
        request.Options.Set(OptionsKey.VersionKey, pending.Version);

        enricher.Enrich(request);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await pending.GetValueTask();
        });
    }
}
