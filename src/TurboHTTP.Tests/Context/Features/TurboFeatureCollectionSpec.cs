using System.Net;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context.Features;
using TurboHTTP.Server;

namespace TurboHTTP.Tests.Context.Features;

public sealed class TurboFeatureCollectionSpec
{
    [Fact(Timeout = 5000)]
    public void Get_should_return_null_for_unset_feature()
    {
        var collection = new TurboFeatureCollection();
        Assert.Null(collection.Get<ITurboRequestFeature>());
    }

    [Fact(Timeout = 5000)]
    public void Set_and_Get_should_round_trip_for_request_feature()
    {
        var collection = new TurboFeatureCollection();
        var feature = new TurboHttpRequestFeature();
        collection.Set<ITurboRequestFeature>(feature);
        Assert.Same(feature, collection.Get<ITurboRequestFeature>());
    }

    [Fact(Timeout = 5000)]
    public void Set_and_Get_should_round_trip_for_response_feature()
    {
        var collection = new TurboFeatureCollection();
        var feature = new TurboHttpResponseFeature();
        collection.Set<ITurboResponseFeature>(feature);
        Assert.Same(feature, collection.Get<ITurboResponseFeature>());
    }

    [Fact(Timeout = 5000)]
    public void Set_and_Get_should_round_trip_for_connection_feature()
    {
        var collection = new TurboFeatureCollection();
        var info = new TurboConnectionInfo(
            "test-connection",
            IPAddress.Loopback,
            12345,
            IPAddress.Loopback,
            80);
        var feature = new TurboHttpConnectionFeature(info);
        collection.Set<ITurboConnectionFeature>(feature);
        Assert.Same(feature, collection.Get<ITurboConnectionFeature>());
    }

    [Fact(Timeout = 5000)]
    public void Set_null_should_clear_feature()
    {
        var collection = new TurboFeatureCollection();
        var feature = new TurboHttpRequestFeature();
        collection.Set<ITurboRequestFeature>(feature);
        collection.Set<ITurboRequestFeature>(null);
        Assert.Null(collection.Get<ITurboRequestFeature>());
    }

    [Fact(Timeout = 5000)]
    public void Get_should_fall_back_to_dictionary_for_unknown_types()
    {
        var collection = new TurboFeatureCollection();
        var feature = new TlsHandshakeFeature { Protocol = System.Security.Authentication.SslProtocols.Tls13 };
        collection.Set<ITlsHandshakeFeature>(feature);
        Assert.Same(feature, collection.Get<ITlsHandshakeFeature>());
    }

    [Fact(Timeout = 5000)]
    public void IFeatureCollection_Get_should_work_for_aspnet_interfaces()
    {
        var collection = new TurboFeatureCollection();
        var feature = new TurboHttpRequestFeature();
        collection.Set<IHttpRequestFeature>(feature);
        IFeatureCollection fc = collection;
        Assert.Same(feature, fc.Get<IHttpRequestFeature>());
    }

    [Fact(Timeout = 5000)]
    public void Same_feature_registered_under_both_interfaces_should_be_retrievable_by_either()
    {
        var collection = new TurboFeatureCollection();
        var feature = new TurboHttpRequestFeature();
        collection.Set<ITurboRequestFeature>(feature);
        collection.Set<IHttpRequestFeature>(feature);
        Assert.Same(feature, collection.Get<ITurboRequestFeature>());
        Assert.Same(feature, collection.Get<IHttpRequestFeature>());
    }

    [Fact(Timeout = 5000)]
    public void IFeatureCollection_indexer_should_work()
    {
        var collection = new TurboFeatureCollection();
        var feature = new TurboHttpRequestFeature();
        IFeatureCollection fc = collection;
        fc[typeof(IHttpRequestFeature)] = feature;
        Assert.Same(feature, fc[typeof(IHttpRequestFeature)]);
    }

    [Fact(Timeout = 5000)]
    public void IFeatureCollection_IsReadOnly_should_be_false()
    {
        IFeatureCollection collection = new TurboFeatureCollection();
        Assert.False(collection.IsReadOnly);
    }
}
