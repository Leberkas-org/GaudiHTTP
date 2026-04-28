using System.Runtime.Versioning;
using Servus.Akka.Diagnostics;

namespace Servus.Akka.IO.Quic;

[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macOS")]
[SupportedOSPlatform("windows")]
public sealed class QuicStreamLease : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly long _createdTicks = Environment.TickCount64;

    public QuicStreamLease(StreamHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        Handle = handle;
    }

    public StreamHandle Handle { get; }

    public RequestEndpoint Key => Handle.Key;

    public bool IsAlive { get; private set; } = true;

    public CancellationToken Token => _cts.Token;

    public void Dispose()
    {
        if (!IsAlive)
        {
            return;
        }

        IsAlive = false;

        _cts.Cancel();
        _cts.Dispose();

        var durationMs = Environment.TickCount64 - _createdTicks;
        var host = Key.Host;
        var port = Key.Port;

        _ = Handle.DisposeAsync().AsTask().ContinueWith(static (t, state) =>
        {
            if (t.IsFaulted)
            {
                var (h, p) = ((string, int))state!;
                ServusTrace.Connection.Warning(null,
                    "QUIC stream to {0}:{1} async disposal failed: {2}", h, p,
                    t.Exception?.InnerException?.Message ?? "unknown");
            }
        }, (host, port), TaskScheduler.Default);

        ServusMetrics.ConnectionDuration.Record(
            durationMs / 1000.0,
            new("server.address", host),
            new("server.port", port));
    }
}