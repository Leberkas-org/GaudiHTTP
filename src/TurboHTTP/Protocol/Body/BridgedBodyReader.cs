using System.Threading.Tasks.Sources;

namespace TurboHTTP.Protocol.Body;

internal sealed class BridgedBodyReader : IBodyReader, IValueTaskSource<BodyReadResult>
{
    private ManualResetValueTaskSourceCore<BodyReadResult> _core;
    private Action? _onConsumed;
    private bool _hasResult;
    private bool _pendingComplete;

    public bool IsBuffered => false;
    public bool IsCompleted { get; private set; }

    public void Reset()
    {
        _onConsumed = null;
        _hasResult = false;
        _pendingComplete = false;
        IsCompleted = false;
        _core = default;
    }

    public void Supply(ReadOnlyMemory<byte> data, Action onConsumed)
    {
        _hasResult = true;
        _onConsumed = onConsumed;
        _core.SetResult(new BodyReadResult(data, isCompleted: false));
    }

    public void Complete()
    {
        if (_hasResult)
        {
            _pendingComplete = true;
            return;
        }

        IsCompleted = true;
        _core.SetResult(new BodyReadResult(default, isCompleted: true));
    }

    public void Fault(Exception ex) => _core.SetException(ex);

    public ValueTask<BodyReadResult> ReadAsync(CancellationToken cancellationToken = default)
        => new(this, _core.Version);

    public void AdvanceTo(int consumed)
    {
        var callback = _onConsumed;
        _onConsumed = null;
        _hasResult = false;
        _core.Reset();
        callback?.Invoke();

        if (_pendingComplete)
        {
            _pendingComplete = false;
            IsCompleted = true;
            _core.SetResult(new BodyReadResult(default, isCompleted: true));
        }
    }

    public ReadOnlyMemory<byte> GetBufferedBody() => throw new NotSupportedException();

    public Stream AsStream() => new BodyBridgeStream(this);

    public void Dispose()
    {
    }

    BodyReadResult IValueTaskSource<BodyReadResult>.GetResult(short token) => _core.GetResult(token);

    ValueTaskSourceStatus IValueTaskSource<BodyReadResult>.GetStatus(short token) => _core.GetStatus(token);

    void IValueTaskSource<BodyReadResult>.OnCompleted(Action<object?> continuation, object? state, short token,
        ValueTaskSourceOnCompletedFlags flags)
        => _core.OnCompleted(continuation, state, token, flags);
}
