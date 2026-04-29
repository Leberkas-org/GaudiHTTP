using System.Runtime.Versioning;

namespace Servus.Akka.Transport.Quic;

#pragma warning disable CA1416

[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macOS")]
[SupportedOSPlatform("windows")]
public sealed class QuicConnectionLease : IDisposable
{
    private readonly long _createdTicks = Environment.TickCount64;
    private bool _alive = true;
    private int _activeStreams;
    private int _maxConcurrentStreams;
    private DateTime _lastActivity = DateTime.UtcNow;

    public QuicConnectionLease(QuicConnectionHandle handle, int maxConcurrentStreams)
    {
        Handle = handle;
        _maxConcurrentStreams = maxConcurrentStreams;
    }

    public QuicConnectionHandle Handle { get; }

    public bool IsAlive() => _alive;

    public bool IsExpired(TimeSpan maxLifetime)
    {
        if (maxLifetime == Timeout.InfiniteTimeSpan)
        {
            return false;
        }

        return Environment.TickCount64 - _createdTicks > (long)maxLifetime.TotalMilliseconds;
    }

    public bool CanAcceptStream() => _alive && _activeStreams < _maxConcurrentStreams;

    public void MarkBusy()
    {
        _activeStreams++;
        _lastActivity = DateTime.UtcNow;
    }

    public void MarkIdle()
    {
        _activeStreams--;
        _lastActivity = DateTime.UtcNow;
    }

    public int ActiveStreams => _activeStreams;

    public DateTime LastActivity => _lastActivity;

    public void Dispose()
    {
        if (!_alive)
        {
            return;
        }

        _alive = false;
        _ = Handle.DisposeAsync().AsTask();
    }
}

#pragma warning restore CA1416
