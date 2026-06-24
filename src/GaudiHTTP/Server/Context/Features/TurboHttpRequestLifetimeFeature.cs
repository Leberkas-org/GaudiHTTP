using Microsoft.AspNetCore.Http.Features;

namespace TurboHTTP.Server.Context.Features;

internal sealed class TurboHttpRequestLifetimeFeature : IHttpRequestLifetimeFeature
{
    [ThreadStatic] private static Stack<CancellationTokenSource>? _ctsPool;

    private const int MaxPoolSize = 64;

    private CancellationTokenSource _cts = RentCts();

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
            ReturnCts(old);
        }
    }

    public void Abort() => _cts.Cancel();

    internal void Reset()
    {
        var old = _cts;
        _cts = RentCts();
        ReturnCts(old);
    }

    private static CancellationTokenSource RentCts()
    {
        if (_ctsPool is { Count: > 0 })
        {
            return _ctsPool.Pop();
        }

        return new CancellationTokenSource();
    }

    private static void ReturnCts(CancellationTokenSource cts)
    {
        if (cts.TryReset())
        {
            _ctsPool ??= new Stack<CancellationTokenSource>(MaxPoolSize);
            if (_ctsPool.Count < MaxPoolSize)
            {
                _ctsPool.Push(cts);
                return;
            }
        }

        cts.Dispose();
    }
}
