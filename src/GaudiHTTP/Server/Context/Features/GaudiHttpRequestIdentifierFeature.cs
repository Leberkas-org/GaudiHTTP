using Microsoft.AspNetCore.Http.Features;

namespace GaudiHTTP.Server.Context.Features;

internal sealed class GaudiHttpRequestIdentifierFeature : IHttpRequestIdentifierFeature
{
    public string TraceIdentifier
    {
        get => field ??= Guid.NewGuid().ToString("N");
        set;
    }

    internal void Reset()
    {
        TraceIdentifier = null!;
    }
}
