using Microsoft.AspNetCore.Http.Features;

namespace TurboHTTP.Context.Features;

internal sealed class TurboHttpRequestLifetimeFeature : IHttpRequestLifetimeFeature
{
    public CancellationToken RequestAborted { get; set; }

    public void Abort() => RequestAborted = new CancellationToken(true);
}
