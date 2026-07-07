using System.Net;
using Servus.Akka.Transport;
using GaudiHTTP.Server;

namespace GaudiHTTP.Tests.Server.Options;

/// <summary>
/// GaudiServerLimits.MaxRequestBufferSize — input threshold projection removed.
/// Servus.akka's rent-and-receive transport is watermark-based and has no configurable
/// per-listener input threshold. The InputPauseThreshold property no longer exists on
/// TcpListenerOptions/QuicListenerOptions.
/// </summary>
public sealed class MaxRequestBufferSizeSpec
{
    // TODO: Reintroduce tests if MaxRequestBufferSize gains a new purpose
    // (e.g., bounds on total request size, not backpressure threshold).
}
