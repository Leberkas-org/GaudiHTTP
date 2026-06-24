namespace GaudiHTTP.Protocol.Syntax.Http2;

/// <summary>
/// Measures the connection's base round-trip time via correlated PINGs and decides when the next
/// measurement PING is due. Actor-confined: no synchronization. Clocked via <see cref="TimeProvider"/>
/// so tests can drive it deterministically with FakeTimeProvider.
/// </summary>
internal sealed class RttEstimator(TimeProvider clock, TimeSpan pingInterval)
{
    private long _pingSentTimestamp;
    private long _lastPingTimestamp;
    private bool _awaitingAck;
    private bool _everPinged;

    /// <summary>Smallest RTT observed so far. <see cref="TimeSpan.Zero"/> means "unknown / no sample yet".</summary>
    public TimeSpan MinRtt { get; private set; } = TimeSpan.Zero;

    /// <summary>True when no measurement is in flight and the interval since the last ping has elapsed.</summary>
    public bool ShouldSendPing()
    {
        if (_awaitingAck)
        {
            return false;
        }

        if (!_everPinged)
        {
            return true;
        }

        return clock.GetElapsedTime(_lastPingTimestamp) >= pingInterval;
    }

    public void OnPingSent()
    {
        _pingSentTimestamp = clock.GetTimestamp();
        _lastPingTimestamp = _pingSentTimestamp;
        _awaitingAck = true;
        _everPinged = true;
    }

    public void OnPingAck()
    {
        if (!_awaitingAck)
        {
            return;
        }

        _awaitingAck = false;
        var rtt = clock.GetElapsedTime(_pingSentTimestamp);
        if (MinRtt == TimeSpan.Zero || rtt < MinRtt)
        {
            MinRtt = rtt;
        }
    }

    public void Reset()
    {
        MinRtt = TimeSpan.Zero;
        _awaitingAck = false;
        _everPinged = false;
        _pingSentTimestamp = 0;
        _lastPingTimestamp = 0;
    }
}
