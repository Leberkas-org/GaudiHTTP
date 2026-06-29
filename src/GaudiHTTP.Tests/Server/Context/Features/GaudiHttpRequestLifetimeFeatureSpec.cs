using GaudiHTTP.Server.Context.Features;

namespace GaudiHTTP.Tests.Server.Context.Features;

public sealed class GaudiHttpRequestLifetimeFeatureSpec
{
    [Fact(Timeout = 5000)]
    public void RequestAborted_should_be_cancellable_token()
    {
        var feature = new GaudiHttpRequestLifetimeFeature();

        Assert.True(feature.RequestAborted.CanBeCanceled);
        Assert.False(feature.RequestAborted.IsCancellationRequested);
    }

    [Fact(Timeout = 5000)]
    public void Abort_should_cancel_RequestAborted_token()
    {
        var feature = new GaudiHttpRequestLifetimeFeature();
        var token = feature.RequestAborted;

        feature.Abort();

        Assert.True(token.IsCancellationRequested);
    }

    [Fact(Timeout = 5000)]
    public void Abort_should_trigger_registered_callbacks()
    {
        var feature = new GaudiHttpRequestLifetimeFeature();
        var called = false;
        feature.RequestAborted.Register(() => called = true);

        feature.Abort();

        Assert.True(called);
    }

    [Fact(Timeout = 5000)]
    public void RequestAborted_setter_should_link_to_external_token()
    {
        var feature = new GaudiHttpRequestLifetimeFeature();
        using var externalCts = new CancellationTokenSource();

        feature.RequestAborted = externalCts.Token;

        Assert.False(feature.RequestAborted.IsCancellationRequested);
        externalCts.Cancel();
        Assert.True(feature.RequestAborted.IsCancellationRequested);
    }

    [Fact(Timeout = 5000)]
    public void Abort_should_cancel_even_when_linked_to_external_token()
    {
        var feature = new GaudiHttpRequestLifetimeFeature();
        using var externalCts = new CancellationTokenSource();
        feature.RequestAborted = externalCts.Token;

        feature.Abort();

        Assert.True(feature.RequestAborted.IsCancellationRequested);
        Assert.False(externalCts.IsCancellationRequested);
    }

    [Fact(Timeout = 5000)]
    public void Reset_should_provide_fresh_uncancelled_token()
    {
        var feature = new GaudiHttpRequestLifetimeFeature();
        feature.Abort();
        Assert.True(feature.RequestAborted.IsCancellationRequested);

        feature.Reset();

        Assert.True(feature.RequestAborted.CanBeCanceled);
        Assert.False(feature.RequestAborted.IsCancellationRequested);
    }
}
