using Microsoft.AspNetCore.Http.Features;

namespace GaudiHTTP.Server.Context.Features;

internal sealed class GaudiHttpRequestBodyDetectionFeature(bool canHaveBody) : IHttpRequestBodyDetectionFeature
{
    public bool CanHaveBody { get; private set; } = canHaveBody;

    internal void Reset(bool canHaveBody)
    {
        CanHaveBody = canHaveBody;
    }
}