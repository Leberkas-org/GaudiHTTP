using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Pooling;
using TurboHTTP.Server;
using TurboHTTP.Server.Context.Features;

namespace TurboHTTP.Tests.Server;

public sealed class ContextPoolingSpec
{
    private readonly ConnectionPoolContext _pool = new();
    private static IFeatureCollection CreateContext(IFeatureCollection? features = null)
    {
        features ??= new FeatureCollection();
        features.Set<IHttpRequestFeature>(new TurboHttpRequestFeature());
        features.Set<IHttpResponseFeature>(new TurboHttpResponseFeature());
        features.Set<IHttpResponseBodyFeature>(new TurboHttpResponseBodyFeature());

        return features;
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpResponseFeature_Reset_clears_status_code()
    {
        var feature = new TurboHttpResponseFeature
        {
            StatusCode = 404
        };

        feature.Reset();

        Assert.Equal(200, feature.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpResponseFeature_Reset_clears_reason_phrase()
    {
        var feature = new TurboHttpResponseFeature
        {
            ReasonPhrase = "Not Found"
        };

        feature.Reset();

        Assert.Null(feature.ReasonPhrase);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpResponseFeature_Reset_clears_headers()
    {
        var feature = new TurboHttpResponseFeature
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
    public void TurboHttpResponseFeature_Reset_clears_callbacks()
    {
        var feature = new TurboHttpResponseFeature();
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
    public void TurboHttpResponseFeature_Reset_clears_has_started()
    {
        var feature = new TurboHttpResponseFeature();
        _ = feature.HasStarted;

        feature.Reset();

        Assert.False(feature.HasStarted);
    }

    [Fact(Timeout = 5000)]
    public void FeatureCollectionFactory_Return_stores_context_in_pool()
    {
        var ctx = FeatureCollectionFactory.Create(_pool, new TurboHttpRequestFeature(), hasBody: false);

        FeatureCollectionFactory.Return(_pool, ctx);

        var ctx2 = FeatureCollectionFactory.Create(
            _pool,
            new TurboHttpRequestFeature(),
            hasBody: false,
            services: null,
            connectionFeature: null);

        Assert.NotNull(ctx2);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpRequestBodyDetectionFeature_Reset_should_update_CanHaveBody()
    {
        var feature = new TurboHttpRequestBodyDetectionFeature(true);
        Assert.True(feature.CanHaveBody);

        feature.Reset(false);

        Assert.False(feature.CanHaveBody);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpRequestIdentifierFeature_Reset_should_clear_trace_identifier()
    {
        var feature = new TurboHttpRequestIdentifierFeature();
        var original = feature.TraceIdentifier;

        feature.Reset();
        var afterReset = feature.TraceIdentifier;

        Assert.NotEqual(original, afterReset);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpMaxRequestBodySizeFeature_Reset_should_restore_defaults()
    {
        var feature = new TurboHttpMaxRequestBodySizeFeature
        {
            IsReadOnly = true,
            MaxRequestBodySize = 999
        };

        feature.Reset(42);

        Assert.False(feature.IsReadOnly);
        Assert.Equal(42, feature.MaxRequestBodySize);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpBodyControlFeature_Reset_should_clear_AllowSynchronousIO()
    {
        var feature = new TurboHttpBodyControlFeature { AllowSynchronousIO = true };

        feature.Reset();

        Assert.False(feature.AllowSynchronousIO);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpRequestLifetimeFeature_Reset_should_provide_fresh_non_cancelled_token()
    {
        var feature = new TurboHttpRequestLifetimeFeature();
        feature.Abort();
        Assert.True(feature.RequestAborted.IsCancellationRequested);

        feature.Reset();

        Assert.False(feature.RequestAborted.IsCancellationRequested);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpRequestLifetimeFeature_Reset_without_cancel_should_not_throw()
    {
        var feature = new TurboHttpRequestLifetimeFeature();
        var token1 = feature.RequestAborted;
        Assert.False(token1.IsCancellationRequested);

        feature.Reset();

        Assert.False(feature.RequestAborted.IsCancellationRequested);
    }

    [Fact(Timeout = 5000)]
    public void FeatureCollectionFactory_should_reuse_response_feature_from_pool()
    {
        var ctx = FeatureCollectionFactory.Create(
            _pool, new TurboHttpRequestFeature(), hasBody: false);
        var originalResponse = ctx.Get<IHttpResponseFeature>();
        originalResponse!.StatusCode = 404;

        FeatureCollectionFactory.Return(_pool, ctx);

        var ctx2 = FeatureCollectionFactory.Create(
            _pool, new TurboHttpRequestFeature(), hasBody: true);
        var reusedResponse = ctx2.Get<IHttpResponseFeature>();

        Assert.Same(originalResponse, reusedResponse);
        Assert.Equal(200, reusedResponse!.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public void FeatureCollectionFactory_should_reuse_lifetime_feature_from_pool()
    {
        var ctx = FeatureCollectionFactory.Create(
            _pool, new TurboHttpRequestFeature(), hasBody: false);
        var originalLifetime = ctx.Get<IHttpRequestLifetimeFeature>();

        FeatureCollectionFactory.Return(_pool, ctx);

        var ctx2 = FeatureCollectionFactory.Create(
            _pool, new TurboHttpRequestFeature(), hasBody: false);
        var reusedLifetime = ctx2.Get<IHttpRequestLifetimeFeature>();

        Assert.Same(originalLifetime, reusedLifetime);
        Assert.False(reusedLifetime!.RequestAborted.IsCancellationRequested);
    }

    [Fact(Timeout = 5000)]
    public void FeatureCollectionFactory_should_recycle_response_body_feature()
    {
        var ctx = FeatureCollectionFactory.Create(
            _pool, new TurboHttpRequestFeature(), hasBody: false);
        var originalBody = ctx.Get<IHttpResponseBodyFeature>();

        FeatureCollectionFactory.Return(_pool, ctx);

        var ctx2 = FeatureCollectionFactory.Create(
            _pool, new TurboHttpRequestFeature(), hasBody: false);
        var recycledBody = ctx2.Get<IHttpResponseBodyFeature>();

        Assert.Same(originalBody, recycledBody);
        Assert.False(((TurboHttpResponseBodyFeature)recycledBody!).HasStarted);
    }

    [Fact(Timeout = 5000)]
    public void FeatureCollectionFactory_should_reuse_same_TurboFeatureCollection_instance_from_pool()
    {
        var ctx1 = FeatureCollectionFactory.Create(_pool, new TurboHttpRequestFeature(), hasBody: false);

        FeatureCollectionFactory.Return(_pool, ctx1);

        var ctx2 = FeatureCollectionFactory.Create(_pool, new TurboHttpRequestFeature(), hasBody: false);

        Assert.Same(ctx1, ctx2);
    }
}