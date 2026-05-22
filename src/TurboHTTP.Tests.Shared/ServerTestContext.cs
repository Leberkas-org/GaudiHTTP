using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using TurboHTTP.Context.Features;
using TurboHTTP.Server;

namespace TurboHTTP.Tests.Shared;

internal static class ServerTestContext
{
    internal static ServerTestContextBuilder Request() => new();

    internal static TurboHttpContext CreateResponse(int statusCode = 200)
    {
        var features = new FeatureCollection();
        features.Set<IHttpRequestFeature>(new TurboHttpRequestFeature());
        var responseFeature = new TurboHttpResponseFeature { StatusCode = statusCode };
        features.Set<IHttpResponseFeature>(responseFeature);
        var bodyFeature = new TurboHttpResponseBodyFeature();
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        features.Set<ITurboResponseBodyFeature>(bodyFeature);
        return new TurboHttpContext(features);
    }

    internal static TurboHttpContext CreateH2Response(int streamId, int statusCode = 200)
    {
        var ctx = CreateResponse(statusCode);
        ctx.Features.Set<IHttp2StreamIdFeature>(new TurboHttp2StreamIdFeature(streamId));
        return ctx;
    }

    internal static TurboHttpContext CreateH3Response(long streamId, int statusCode = 200)
    {
        var ctx = CreateResponse(statusCode);
        ctx.Features.Set<ITurboHttp3StreamIdFeature>(new TurboHttp3StreamIdFeature(streamId));
        return ctx;
    }
}
