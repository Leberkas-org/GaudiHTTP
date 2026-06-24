using Microsoft.AspNetCore.Http.Features;

namespace GaudiHTTP.Server.Context.Features;

internal sealed class GaudiHttpResetFeature(Action<int> resetCallback) : IHttpResetFeature
{
    public void Reset(int errorCode) => resetCallback(errorCode);
}
