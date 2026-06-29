using GaudiHTTP.Server.Context.Features;

namespace GaudiHTTP.Tests.Server.Context;

public sealed class GaudiHttpResponseFeatureSpec
{
    [Fact(Timeout = 5000)]
    public void StatusCode_should_default_to_200()
    {
        var feature = new GaudiHttpResponseFeature();
        Assert.Equal(200, feature.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public void StatusCode_should_be_settable()
    {
        var feature = new GaudiHttpResponseFeature
        {
            StatusCode = 404
        };
        Assert.Equal(404, feature.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public void ReasonPhrase_should_default_to_null()
    {
        var feature = new GaudiHttpResponseFeature();
        Assert.Null(feature.ReasonPhrase);
    }

    [Fact(Timeout = 5000)]
    public void ReasonPhrase_should_be_settable()
    {
        var feature = new GaudiHttpResponseFeature
        {
            ReasonPhrase = "All Good"
        };
        Assert.Equal("All Good", feature.ReasonPhrase);
    }

    [Fact(Timeout = 5000)]
    public void HasStarted_should_be_false_initially()
    {
        var feature = new GaudiHttpResponseFeature();
        Assert.False(feature.HasStarted);
    }

    [Fact(Timeout = 5000)]
    public void Headers_should_return_IHeaderDictionary()
    {
        var feature = new GaudiHttpResponseFeature
        {
            Headers =
            {
                ["X-Custom"] = "value"
            }
        };
        Assert.Equal("value", feature.Headers["X-Custom"].ToString());
    }

    [Fact(Timeout = 5000)]
    public async Task OnStarting_should_invoke_callback()
    {
        var feature = new GaudiHttpResponseFeature();
        var called = false;
        feature.OnStarting(_ => { called = true; return Task.CompletedTask; }, null!);
        await feature.FireOnStartingAsync();
        Assert.True(called);
    }

    [Fact(Timeout = 5000)]
    public async Task OnCompleted_should_invoke_callback()
    {
        var feature = new GaudiHttpResponseFeature();
        var called = false;
        feature.OnCompleted(_ => { called = true; return Task.CompletedTask; }, null!);
        await feature.FireOnCompletedAsync();
        Assert.True(called);
    }
}
