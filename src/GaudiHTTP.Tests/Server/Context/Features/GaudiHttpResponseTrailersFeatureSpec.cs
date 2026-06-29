using GaudiHTTP.Server.Context.Features;
using Microsoft.AspNetCore.Http;

namespace GaudiHTTP.Tests.Server.Context.Features;

public sealed class GaudiHttpResponseTrailersFeatureSpec
{
    [Fact(Timeout = 5000)]
    public void Trailers_should_default_to_empty_owned_dictionary()
    {
        var feature = new GaudiHttpResponseTrailersFeature();
        Assert.Empty(feature.Trailers);
    }

    [Fact(Timeout = 5000)]
    public void AppendTrailer_through_getter_should_be_visible()
    {
        var feature = new GaudiHttpResponseTrailersFeature();
        feature.Trailers["grpc-status"] = "0";
        Assert.Equal("0", feature.Trailers["grpc-status"].ToString());
    }

    [Fact(Timeout = 5000)]
    public void Trailers_setter_should_store_assigned_dictionary()
    {
        // Kestrel parity (Http2Stream: _userTrailers = value, getter returns _userTrailers ??
        // base.ResponseTrailers): a wholesale assignment is honored, not silently dropped.
        var feature = new GaudiHttpResponseTrailersFeature();
        var custom = new HeaderDictionary { ["grpc-status"] = "0" };

        feature.Trailers = custom;

        Assert.Same(custom, feature.Trailers);
    }

    [Fact(Timeout = 5000)]
    public void Trailers_setter_should_throw_on_null()
    {
        var feature = new GaudiHttpResponseTrailersFeature();
        Assert.Throws<ArgumentNullException>(() => feature.Trailers = null!);
    }

    [Fact(Timeout = 5000)]
    public void GetAllowedTrailers_should_reflect_assigned_dictionary()
    {
        var feature = new GaudiHttpResponseTrailersFeature();
        feature.Trailers = new HeaderDictionary
        {
            ["grpc-status"] = "0",
            ["Content-Length"] = "5" // not allowed in trailers — must be filtered out
        };

        var allowed = feature.GetAllowedTrailers().Select(h => h.Key).ToList();

        Assert.Contains("grpc-status", allowed);
        Assert.DoesNotContain("Content-Length", allowed);
    }

    [Fact(Timeout = 5000)]
    public void Reset_should_clear_assigned_trailers_override()
    {
        var feature = new GaudiHttpResponseTrailersFeature();
        feature.Trailers = new HeaderDictionary { ["grpc-status"] = "0" };

        feature.Reset();

        Assert.Empty(feature.Trailers);
    }
}
