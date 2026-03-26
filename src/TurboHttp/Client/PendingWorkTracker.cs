using System.Threading;
using Akka.Event;

namespace TurboHttp.Client;

/// <summary>
/// Thread-safe implementation of <see cref="IPendingWorkTracker"/> that counts
/// in-flight re-injections from feature BidiStages. Uses <see cref="Interlocked"/>
/// for lock-free atomic updates safe for concurrent Akka dispatcher threads.
/// </summary>
internal sealed class PendingWorkTracker : IPendingWorkTracker
{
    private readonly ILoggingAdapter? _logger;
    private int _count;

    /// <summary>Creates a new tracker with optional DEBUG-level logging.</summary>
    /// <param name="logger">When non-null, logs increment/decrement events at DEBUG level.</param>
    public PendingWorkTracker(ILoggingAdapter? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public void IncrementPending()
    {
        var newValue = Interlocked.Increment(ref _count);
        _logger?.Debug("PendingWorkTracker: pending work incremented to {0}", newValue);
    }

    /// <inheritdoc />
    public void DecrementPending()
    {
        var newValue = Interlocked.Decrement(ref _count);
        if (newValue < 0)
        {
            // Self-heal: don't leave counter negative; reset to 0.
            Interlocked.CompareExchange(ref _count, 0, newValue);
            _logger?.Warning("PendingWorkTracker: decrement below zero (corrected to 0)");
            return;
        }

        _logger?.Debug("PendingWorkTracker: pending work decremented to {0}", newValue);
    }

    /// <inheritdoc />
    public bool IsPending => Volatile.Read(ref _count) > 0;

    /// <summary>Current pending work count. Useful for diagnostics and testing.</summary>
    public int Count => Volatile.Read(ref _count);
}
