using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context.Features;

namespace TurboHTTP.Server;

internal static class ServerContextFactory
{
    public static TurboHttpContext Create(TurboHttpRequestFeature requestFeature, bool hasBody)
    {
        var features = new FeatureCollection();
        features.Set<IHttpRequestFeature>(requestFeature);

        var bodyFeature = new TurboRequestBodyFeature { Body = requestFeature.Body };
        features.Set<ITurboRequestBodyFeature>(bodyFeature);

        features.Set<IHttpResponseFeature>(new TurboHttpResponseFeature());
        features.Set<IHttpRequestBodyDetectionFeature>(new TurboHttpRequestBodyDetectionFeature(hasBody));
        var responseBodyFeature = new TurboHttpResponseBodyFeature();
        features.Set<IHttpResponseBodyFeature>(responseBodyFeature);
        features.Set<ITurboResponseBodyFeature>(responseBodyFeature);
        return new TurboHttpContext(features);
    }
}
