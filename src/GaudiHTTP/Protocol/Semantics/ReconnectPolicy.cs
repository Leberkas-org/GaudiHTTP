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
    /// <summary>Timer name used to defer a reconnect attempt behind an exponential backoff delay.</summary>
    public const string BackoffTimerName = ReconnectBackoff.TimerName;

    private readonly IClientStageOperations _ops;
    private readonly int _maxAttempts;
    private readonly TimeSpan _initialBackoff;
    private readonly TimeSpan _maxBackoff;
    private readonly double _multiplier;
    private readonly double _jitter;
    private readonly Random _rng;
    private int _attempts;
    private TBuffered? _buffered;
    private TransportOptions? _transportOptions;

    /// <param name="initialBackoff">
    /// Delay before the first retry after a failed attempt. When less than or equal to
    /// <see cref="TimeSpan.Zero"/> (the default) reconnects fire immediately, preserving the legacy
    /// zero-delay behaviour; a positive value defers each retry behind <see cref="BackoffTimerName"/>.
    /// </param>
    /// <param name="maxBackoff">Upper bound on the (pre-jitter) exponential backoff delay.</param>
    /// <param name="multiplier">Growth factor applied per successive retry.</param>
    /// <param name="jitter">Fractional jitter (0..1) applied symmetrically to each computed delay.</param>
    public ReconnectPolicy(
        IClientStageOperations ops,
        int maxAttempts,
        TimeSpan initialBackoff = default,
        TimeSpan maxBackoff = default,
        double multiplier = 2.0,
        double jitter = 0.2,
        Random? rng = null)
    {
        _ops = ops ?? throw new ArgumentNullException(nameof(ops));
        _maxAttempts = maxAttempts;
        _initialBackoff = initialBackoff;
        _maxBackoff = maxBackoff > TimeSpan.Zero ? maxBackoff : initialBackoff;
        _multiplier = multiplier <= 0 ? 1.0 : multiplier;
        _jitter = Math.Clamp(jitter, 0.0, 1.0);
        _rng = rng ?? Random.Shared;
    }

    public bool CanReconnect => _maxAttempts > 0;

    public int Attempts => _attempts;

    /// <summary>Non-destructive peek at the currently buffered work (or default if none).</summary>
    public TBuffered? Buffered => _buffered;

    /// <summary>
    /// Begins reconnecting: stashes <paramref name="buffered"/> for later replay/failure, resets
    /// the attempt counter to 1, and emits the first <see cref="ConnectTransport"/>. The first
    /// attempt fires immediately (a real disconnect just occurred); only subsequent retries via
    /// <see cref="OnAttemptFailed"/> are spaced by the backoff.
    /// </summary>
    public void Start(TBuffered buffered, TransportOptions transportOptions)
    {
        _buffered = buffered;
        _transportOptions = transportOptions;
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
        _transportOptions = default;
        if (_initialBackoff > TimeSpan.Zero)
        {
            _ops.OnCancelTimer(BackoffTimerName);
        }

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
        _transportOptions = transportOptions;

        if (_initialBackoff > TimeSpan.Zero)
        {
            // Defer the retry behind a backoff timer instead of reconnecting immediately, so a
            // connection-refused peer is not hammered in a tight loop (see OnReconnectTimerFired).
            _ops.OnScheduleTimer(BackoffTimerName, ComputeBackoff(_attempts));
        }
        else
        {
            _ops.OnOutbound(new ConnectTransport(transportOptions));
        }

        return false;
    }

    /// <summary>
    /// Emits the deferred <see cref="ConnectTransport"/> when the backoff timer named
    /// <see cref="BackoffTimerName"/> fires. Returns <see langword="false"/> for any other timer so
    /// the caller can continue dispatching, and is a no-op if the reconnect was already abandoned.
    /// </summary>
    public bool OnReconnectTimerFired(string name)
    {
        if (name != BackoffTimerName)
        {
            return false;
        }

        if (_transportOptions is not null)
        {
            _ops.OnOutbound(new ConnectTransport(_transportOptions));
        }

        return true;
    }

    // _attempts is post-increment: 2 for the first retry, 3 for the second, ... so retryNumber = attempts - 1.
    private TimeSpan ComputeBackoff(int attempt)
        => ReconnectBackoff.Compute(attempt - 1, _initialBackoff, _maxBackoff, _multiplier, _jitter, _rng);
}
