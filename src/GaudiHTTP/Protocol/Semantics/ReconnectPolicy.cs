using Servus.Akka.Transport;
using GaudiHTTP.Streams.Stages.Client;

namespace GaudiHTTP.Protocol.Semantics;

/// <summary>
/// Owns the attempt-counting mechanics shared by the HTTP/1.0 and HTTP/1.1 client reconnect
/// trio (start / restore / attempt-failed), generic over whatever work the caller buffers while
/// reconnecting (a single request for HTTP/1.0, a request queue for HTTP/1.1). This class does
/// NOT own connection lifecycle state — callers keep their own state (a bool, or an explicit
/// enum) and only ask this policy "how many attempts are left" / "emit the next transport op".
/// </summary>
internal sealed class ReconnectPolicy<TBuffered>
{
    private readonly IClientStageOperations _ops;
    private readonly int _maxAttempts;
    private int _attempts;
    private TBuffered? _buffered;

    public ReconnectPolicy(IClientStageOperations ops, int maxAttempts)
    {
        _ops = ops ?? throw new ArgumentNullException(nameof(ops));
        _maxAttempts = maxAttempts;
    }

    public bool CanReconnect => _maxAttempts > 0;

    public int Attempts => _attempts;

    /// <summary>Non-destructive peek at the currently buffered work (or default if none).</summary>
    public TBuffered? Buffered => _buffered;

    /// <summary>
    /// Begins reconnecting: stashes <paramref name="buffered"/> for later replay/failure, resets
    /// the attempt counter to 1, and emits the first <see cref="ConnectTransport"/>.
    /// </summary>
    public void Start(TBuffered buffered, TransportOptions transportOptions)
    {
        _buffered = buffered;
        _attempts = 1;
        _ops.OnOutbound(new ConnectTransport(transportOptions));
    }

    /// <summary>
    /// Takes ownership of whatever was buffered, resetting the attempt counter. Used both when a
    /// reconnect succeeds (caller replays the buffered work) and when it is abandoned outright
    /// (caller fails the buffered work) — the two cases are distinguished by what the caller does
    /// with the returned value, not by this method.
    /// </summary>
    public TBuffered? TakeBuffered()
    {
        var buffered = _buffered;
        _buffered = default;
        _attempts = 0;
        return buffered;
    }

    /// <summary>
    /// Records a failed reconnect attempt. If <see cref="_maxAttempts"/> is now exhausted, takes
    /// ownership of the buffered work (via <paramref name="buffered"/>) and emits a
    /// <see cref="DisconnectTransport"/>, returning <see langword="true"/> so the caller can fail
    /// the buffered work and move its own lifecycle state to "dead". Otherwise increments the
    /// attempt counter and emits the next <see cref="ConnectTransport"/>, returning
    /// <see langword="false"/>.
    /// </summary>
    public bool OnAttemptFailed(TransportOptions transportOptions, out TBuffered? buffered)
    {
        if (_attempts >= _maxAttempts)
        {
            buffered = TakeBuffered();
            _ops.OnOutbound(new DisconnectTransport(DisconnectReason.Error));
            return true;
        }

        buffered = default;
        _attempts++;
        _ops.OnOutbound(new ConnectTransport(transportOptions));
        return false;
    }
}
