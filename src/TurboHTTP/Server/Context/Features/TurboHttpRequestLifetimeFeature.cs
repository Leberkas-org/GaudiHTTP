using Microsoft.AspNetCore.Http.Features;

namespace TurboHTTP.Server.Context.Features;

internal sealed class TurboHttpRequestLifetimeFeature : IHttpRequestLifetimeFeature
{
    private CancellationTokenSource _cts = new();

    public CancellationToken RequestAborted
    {
        get => _cts.Token;
        set
        {
            if (value == _cts.Token)
            {
                return;
            }

            var old = _cts;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(value);
            old.Dispose();
        }
    }

    public void Abort() => _cts.Cancel();

    internal void Reset()
    {
        _cts.Dispose();
        _cts = new CancellationTokenSource();
    }
}
