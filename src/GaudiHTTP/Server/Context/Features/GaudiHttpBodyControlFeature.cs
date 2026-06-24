using Microsoft.AspNetCore.Http.Features;

namespace GaudiHTTP.Server.Context.Features;

internal sealed class GaudiHttpBodyControlFeature : IHttpBodyControlFeature
{
    public bool AllowSynchronousIO { get; set; }

    internal void Reset()
    {
        AllowSynchronousIO = false;
    }
}
