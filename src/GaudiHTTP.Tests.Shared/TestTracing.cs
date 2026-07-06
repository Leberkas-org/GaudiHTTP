using Servus.Akka.Diagnostics;

namespace GaudiHTTP.Tests.Shared;

/// <summary>
/// Process-wide root listener for Senf tracing in tests. <see cref="Servus.Senf"/> holds exactly
/// one listener, so a spec that called <c>Tracing.Configure</c> directly used to silently replace
/// the logger bridge installed by <see cref="ActorSystemFixture"/> — Warnings from every other
/// test vanished from stdout while that spec ran. Specs that need an extra sink (ring buffers,
/// fault dumps) must register it additively instead:
/// <code>using var scope = TestTracing.Root.Add(myListener);</code>
/// </summary>
public static class TestTracing
{
    public static readonly CompositeTraceListener Root = new();
}
