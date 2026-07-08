namespace GaudiHTTP.Protocol.Semantics;

/// <summary>
/// Shared exponential-backoff-with-jitter calculation for client reconnect retries, used by the
/// HTTP/1.x <see cref="ReconnectPolicy{TBuffered}"/> and the HTTP/2 &amp; HTTP/3 state machines so all
/// protocols space their reconnect attempts identically. Spacing retries (rather than reconnecting
/// with zero delay) avoids a tight busy-loop against a peer that refuses connections instantly, and
/// the jitter avoids a thundering herd of correlated reconnects across connections.
/// </summary>
internal static class ReconnectBackoff
{
    /// <summary>Timer name under which a deferred reconnect attempt is scheduled.</summary>
    public const string TimerName = "reconnect-backoff";

    /// <summary>
    /// Computes the delay before the <paramref name="retryNumber"/>-th reconnect retry (1-based:
    /// 1 = the first retry after the initial immediate attempt). The delay grows geometrically by
    /// <paramref name="multiplier"/> from <paramref name="initial"/>, is capped at
    /// <paramref name="max"/>, then has symmetric <paramref name="jitter"/> applied. Never returns
    /// less than 1 ms so progress is always made.
    /// </summary>
    public static TimeSpan Compute(
        int retryNumber,
        TimeSpan initial,
        TimeSpan max,
        double multiplier,
        double jitter,
        Random rng)
    {
        var cap = max > TimeSpan.Zero ? max : initial;
        var exponent = Math.Max(0, retryNumber - 1);
        var growth = multiplier <= 0 ? 1.0 : multiplier;
        var scaled = initial.TotalMilliseconds * Math.Pow(growth, exponent);
        var capped = Math.Min(scaled, cap.TotalMilliseconds);

        var clampedJitter = Math.Clamp(jitter, 0.0, 1.0);
        var factor = 1.0 + clampedJitter * (2.0 * rng.NextDouble() - 1.0);
        var jittered = Math.Min(capped * factor, cap.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(Math.Max(1.0, jittered));
    }
}
