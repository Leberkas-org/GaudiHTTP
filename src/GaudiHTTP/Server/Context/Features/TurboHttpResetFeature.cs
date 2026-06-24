using Microsoft.AspNetCore.Http.Features;

namespace TurboHTTP.Server.Context.Features;

internal sealed class TurboHttpResetFeature(Action<int> resetCallback) : IHttpResetFeature
{
    public void Reset(int errorCode) => resetCallback(errorCode);
}
