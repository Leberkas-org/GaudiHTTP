using Microsoft.AspNetCore.Http.Features;
using GaudiHTTP.Server.Context.Features;

namespace GaudiHTTP.Tests.Shared;

internal static class ServerTestContext
{
    internal static IFeatureCollection CreateResponse(int statusCode = 200)
    {
        var features = new TurboFeatureCollection();
        features.Set<IHttpRequestFeature>(new GaudiHttpRequestFeature());
        var responseFeature = new GaudiHttpResponseFeature { StatusCode = statusCode };
        features.Set<IHttpResponseFeature>(responseFeature);
        var bodyFeature = new GaudiHttpResponseBodyFeature();
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        return features;
    }

    internal static IFeatureCollection CreateH3Response(long streamId, int statusCode = 200)
    {
        var features = CreateResponse(statusCode);
        features.Set<IHttpStreamIdFeature>(new TurboStreamIdFeature(streamId));
        return features;
    }
}