using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Server.Context.Features;

namespace TurboHTTP.Tests.Server.Context.Features;

public sealed class TurboHttpRequestTrailersFeatureSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.5")]
    public void Available_should_be_false_initially()
    {
        var feature = new TurboHttpRequestTrailersFeature();

        Assert.False(feature.Available);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.5")]
    public void Trailers_should_throw_when_not_available()
    {
        var feature = new TurboHttpRequestTrailersFeature();

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            var _ = feature.Trailers;
        });
        Assert.Contains("not yet available", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.5")]
    public void SetAvailable_should_make_trailers_accessible()
    {
        var feature = new TurboHttpRequestTrailersFeature();
        var trailers = new List<(string Name, string Value)>
        {
            ("x-checksum", "abc123"),
            ("x-timing", "42ms")
        };

        feature.SetAvailable(trailers);

        Assert.True(feature.Available);
        Assert.Equal("abc123", feature.Trailers["x-checksum"]);
        Assert.Equal("42ms", feature.Trailers["x-timing"]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.5")]
    public void SetAvailable_should_filter_prohibited_fields()
    {
        var feature = new TurboHttpRequestTrailersFeature();
        var trailers = new List<(string Name, string Value)>
        {
            ("x-checksum", "abc123"),
            ("transfer-encoding", "chunked")
        };

        feature.SetAvailable(trailers);

        Assert.True(feature.Trailers.ContainsKey("x-checksum"));
        Assert.False(feature.Trailers.ContainsKey("transfer-encoding"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.5")]
    public void SetAvailable_with_empty_list_should_set_available_true()
    {
        var feature = new TurboHttpRequestTrailersFeature();

        feature.SetAvailable([]);

        Assert.True(feature.Available);
        Assert.Empty(feature.Trailers);
    }

    [Fact(Timeout = 5000)]
    public void Reset_should_clear_state()
    {
        var feature = new TurboHttpRequestTrailersFeature();
        feature.SetAvailable([("x-test", "value")]);

        feature.Reset();

        Assert.False(feature.Available);
        Assert.Throws<InvalidOperationException>(() =>
        {
            var _ = feature.Trailers;
        });
    }
}
