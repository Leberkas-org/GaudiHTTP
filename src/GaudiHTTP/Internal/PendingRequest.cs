using System.Threading.Tasks.Sources;
using Servus.Akka.Transport;

namespace GaudiHTTP.Internal;

internal sealed class PendingRequest : IValueTaskSource<HttpResponseMessage>
{
    private static readonly ObjectPool<PendingRequest> Pool = new(256);

    private ManualResetValueTaskSourceCore<HttpResponseMessage> _core = new() { RunContinuationsAsynchronously = true };

    // The channel-path default-timeout source (RequestEnricher rule 8). Disposed when the request
    // completes so its TimerQueue entry is released immediately instead of lingering for the whole
    // timeout window after every request (the channel path never returns the pending to the pool).
    private CancellationTokenSource? _timeoutCts;

    private PendingRequest()
    {
    }

    public static PendingRequest Rent()
    {
        if (!Pool.TryRent(out var item))
        {
            item = new PendingRequest();
        }

        item._core.Reset();
        item._timeoutCts = null;
        return item;
    }

    public static void Return(PendingRequest item)
    {
        item.DisposeTimeoutCts();
        Pool.Return(item);
    }

    /// <summary>
    /// Attaches the channel-path default-timeout source so it is disposed when this request completes
    /// (response delivered, faulted, or returned to the pool) rather than lingering until its timer fires.
    /// </summary>
    public void AttachTimeoutCts(CancellationTokenSource cts) => _timeoutCts = cts;

    private void DisposeTimeoutCts()
    {
        var cts = _timeoutCts;
        _timeoutCts = null;
        cts?.Dispose();
    }

    public short Version => _core.Version;

    public ValueTask<HttpResponseMessage> GetValueTask() => new(this, _core.Version);

    public bool TrySetResult(HttpResponseMessage response, short expectedVersion)
    {
        if (_core.Version != expectedVersion)
        {
            return false;
        }

        try
        {
            _core.SetResult(response);
            DisposeTimeoutCts();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public bool TrySetException(Exception exception, short expectedVersion)
    {
        if (_core.Version != expectedVersion)
        {
            return false;
        }

        try
        {
            _core.SetException(exception);
            DisposeTimeoutCts();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public bool TrySetCanceled(CancellationToken ct = default)
    {
        try
        {
            _core.SetException(new OperationCanceledException(ct));
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public HttpResponseMessage GetResult(short token) => _core.GetResult(token);
    public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);

    public void OnCompleted(Action<object?> continuation, object? state, short token,
        ValueTaskSourceOnCompletedFlags flags)
        => _core.OnCompleted(continuation, state, token, flags);
}
