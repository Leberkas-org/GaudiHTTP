using Microsoft.AspNetCore.Http.Features;
using GaudiHTTP.Pooling;
using GaudiHTTP.Server;
using GaudiHTTP.Server.Context.Features;

namespace GaudiHTTP.Tests.Server;

public sealed class ContextPoolingSpec
{
    private readonly ConnectionPoolContext _pool = new();

    [Fact(Timeout = 5000)]
    public void GaudiHttpResponseFeature_Reset_clears_status_code()
    {
        var feature = new GaudiHttpResponseFeature
        {
            StatusCode = 404
        };

        feature.Reset();

        Assert.Equal(200, feature.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public void GaudiHttpResponseFeature_Reset_clears_reason_phrase()
    {
        var feature = new GaudiHttpResponseFeature
        {
            ReasonPhrase = "Not Found"
        };

        feature.Reset();

        Assert.Null(feature.ReasonPhrase);
    }

    [Fact(Timeout = 5000)]
    public void GaudiHttpResponseFeature_Reset_clears_headers()
    {
        var feature = new GaudiHttpResponseFeature
        {
            Headers =
            {
                ["Content-Type"] = "application/json"
            }
        };

        feature.Reset();

        Assert.Empty(feature.Headers);
    }

    [Fact(Timeout = 5000)]
    public void GaudiHttpResponseFeature_Reset_clears_callbacks()
    {
        var feature = new GaudiHttpResponseFeature();
        var callbackCalled = false;

        feature.OnStarting((_) =>
        {
            callbackCalled = true;
            return Task.CompletedTask;
        }, null!);

        feature.Reset();

        Assert.False(callbackCalled);
    }

    [Fact(Timeout = 5000)]
    public void GaudiHttpResponseFeature_Reset_clears_has_started()
    {
        var feature = new GaudiHttpResponseFeature();
        _ = feature.HasStarted;

        feature.Reset();

        Assert.False(feature.HasStarted);
    }

    [Fact(Timeout = 5000)]
    public void FeatureCollectionFactory_Return_stores_context_in_pool()
    {
        var ctx = FeatureCollectionFactory.Create(_pool, new GaudiHttpRequestFeature(), hasBody: false);

        FeatureCollectionFactory.Return(_pool, ctx);

        var ctx2 = FeatureCollectionFactory.Create(
            _pool,
            new GaudiHttpRequestFeature(),
            hasBody: false,
            connectionFeature: null);

        Assert.NotNull(ctx2);
    }

    [Fact(Timeout = 5000)]
    public void GaudiHttpRequestBodyDetectionFeature_Reset_should_update_CanHaveBody()
    {
        var feature = new GaudiHttpRequestBodyDetectionFeature(true);
        Assert.True(feature.CanHaveBody);

        feature.Reset(false);

        Assert.False(feature.CanHaveBody);
    }

    [Fact(Timeout = 5000)]
    public void GaudiHttpRequestIdentifierFeature_Reset_should_clear_trace_identifier()
    {
        var feature = new GaudiHttpRequestIdentifierFeature();
        var original = feature.TraceIdentifier;

        feature.Reset();
        var afterReset = feature.TraceIdentifier;

        Assert.NotEqual(original, afterReset);
    }

    [Fact(Timeout = 5000)]
    public void GaudiHttpMaxRequestBodySizeFeature_Reset_should_restore_defaults()
    {
        var feature = new GaudiHttpMaxRequestBodySizeFeature
        {
            IsReadOnly = true,
            MaxRequestBodySize = 999
        };

        feature.Reset(42);

        Assert.False(feature.IsReadOnly);
        Assert.Equal(42, feature.MaxRequestBodySize);
    }

    [Fact(Timeout = 5000)]
    public void GaudiHttpBodyControlFeature_Reset_should_clear_AllowSynchronousIO()
    {
        var feature = new GaudiHttpBodyControlFeature { AllowSynchronousIO = true };

        feature.Reset();

        Assert.False(feature.AllowSynchronousIO);
    }

    [Fact(Timeout = 5000)]
    public void GaudiHttpRequestLifetimeFeature_Reset_should_provide_fresh_non_cancelled_token()
    {
        var feature = new GaudiHttpRequestLifetimeFeature();
        feature.Abort();
        Assert.True(feature.RequestAborted.IsCancellationRequested);

        feature.Reset();

        Assert.False(feature.RequestAborted.IsCancellationRequested);
    }

    [Fact(Timeout = 5000)]
    public void GaudiHttpRequestLifetimeFeature_Reset_without_cancel_should_not_throw()
    {
        var feature = new GaudiHttpRequestLifetimeFeature();
        var token1 = feature.RequestAborted;
        Assert.False(token1.IsCancellationRequested);

        feature.Reset();

        Assert.False(feature.RequestAborted.IsCancellationRequested);
    }

    [Fact(Timeout = 5000)]
    public void FeatureCollectionFactory_should_reuse_response_feature_from_pool()
    {
        var ctx = FeatureCollectionFactory.Create(
            _pool, new GaudiHttpRequestFeature(), hasBody: false);
        var originalResponse = ctx.Get<IHttpResponseFeature>();
        originalResponse!.StatusCode = 404;

        FeatureCollectionFactory.Return(_pool, ctx);

        var ctx2 = FeatureCollectionFactory.Create(
            _pool, new GaudiHttpRequestFeature(), hasBody: true);
        var reusedResponse = ctx2.Get<IHttpResponseFeature>();

        Assert.Same(originalResponse, reusedResponse);
        Assert.Equal(200, reusedResponse!.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public void FeatureCollectionFactory_should_reuse_lifetime_feature_from_pool()
    {
        var ctx = FeatureCollectionFactory.Create(
            _pool, new GaudiHttpRequestFeature(), hasBody: false);
        var originalLifetime = ctx.Get<IHttpRequestLifetimeFeature>();

        FeatureCollectionFactory.Return(_pool, ctx);

        var ctx2 = FeatureCollectionFactory.Create(
            _pool, new GaudiHttpRequestFeature(), hasBody: false);
        var reusedLifetime = ctx2.Get<IHttpRequestLifetimeFeature>();

        Assert.Same(originalLifetime, reusedLifetime);
        Assert.False(reusedLifetime!.RequestAborted.IsCancellationRequested);
    }

    [Fact(Timeout = 5000)]
    public void FeatureCollectionFactory_should_recycle_response_body_feature()
    {
        var ctx = FeatureCollectionFactory.Create(
            _pool, new GaudiHttpRequestFeature(), hasBody: false);
        var originalBody = ctx.Get<IHttpResponseBodyFeature>();

        FeatureCollectionFactory.Return(_pool, ctx);

        var ctx2 = FeatureCollectionFactory.Create(
            _pool, new GaudiHttpRequestFeature(), hasBody: false);
        var recycledBody = ctx2.Get<IHttpResponseBodyFeature>();

        Assert.Same(originalBody, recycledBody);
        Assert.False(((GaudiHttpResponseBodyFeature)recycledBody!).HasStarted);
    }

    [Fact(Timeout = 5000)]
    public void FeatureCollectionFactory_should_reuse_same_TurboFeatureCollection_instance_from_pool()
    {
        var ctx1 = FeatureCollectionFactory.Create(_pool, new GaudiHttpRequestFeature(), hasBody: false);

        FeatureCollectionFactory.Return(_pool, ctx1);

        var ctx2 = FeatureCollectionFactory.Create(_pool, new GaudiHttpRequestFeature(), hasBody: false);

        Assert.Same(ctx1, ctx2);
    }
}