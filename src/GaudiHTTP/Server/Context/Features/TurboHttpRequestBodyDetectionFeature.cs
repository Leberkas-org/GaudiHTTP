using Microsoft.AspNetCore.Http.Features;

namespace TurboHTTP.Server.Context.Features;

internal sealed class TurboHttpRequestBodyDetectionFeature : IHttpRequestBodyDetectionFeature
{
    public bool CanHaveBody { get; private set; }

    public TurboHttpRequestBodyDetectionFeature(bool canHaveBody)
    {
        CanHaveBody = canHaveBody;
    }

    internal void Reset(bool canHaveBody)
    {
        CanHaveBody = canHaveBody;
    }
}