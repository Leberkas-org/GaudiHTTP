using Microsoft.AspNetCore.Http.Features;

namespace GaudiHTTP.Server.Context.Features;

internal sealed class GaudiHttpRequestBodyDetectionFeature : IHttpRequestBodyDetectionFeature
{
    public bool CanHaveBody { get; private set; }

    public GaudiHttpRequestBodyDetectionFeature(bool canHaveBody)
    {
        CanHaveBody = canHaveBody;
    }

    internal void Reset(bool canHaveBody)
    {
        CanHaveBody = canHaveBody;
    }
}