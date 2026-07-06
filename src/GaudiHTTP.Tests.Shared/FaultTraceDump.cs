using Servus.Diagnostics;
using Xunit;

namespace GaudiHTTP.Tests.Shared;

/// <summary>
/// Suite-wide stall post-mortem: a process-global trace ring that any spec base can dump when a
/// test fails (including <c>[Fact(Timeout)]</c> watchdogs). The H2 pool-poisoning hunt showed
/// these flakes only reproduce 1-in-N full-suite runs, so the evidence must be captured on the
/// run where it happens — call <see cref="DumpIfTestFailedAsync"/> at the START of the spec
/// base's <c>DisposeAsync</c>, before client/server teardown floods the ring.
/// </summary>
public static class FaultTraceDump
{
    // No category filter: the QUIC layer traces under "Pool"/"Connection", bodies under
    // "ContentEncoding" — a stall can hide in any of them, and the low-traffic categories are
    // noise only in volume terms the ring absorbs anyway.
    private static readonly RingBufferTraceListener Ring = new(capacity: 128 * 1024);

    static FaultTraceDump()
    {
        // Self-sufficient for suites that never configure tracing (End2End, Server); idempotent
        // with ActorSystemFixture, which installs the same composite root.
        Servus.Senf.Tracing.Configure(TestTracing.Root, TraceLevel.Trace);
        TestTracing.Root.Add(Ring);
    }

    public static async ValueTask DumpIfTestFailedAsync()
    {
        if (TestContext.Current.TestState?.Result != TestResult.Failed)
        {
            return;
        }

        var testName = TestContext.Current.Test?.TestDisplayName ?? "unknown-test";
        var segments = testName.Split('.');
        var shortName = segments.Length >= 2 ? $"{segments[^2]}.{segments[^1]}" : testName;
        var safeName = string.Join("_", shortName.Split(Path.GetInvalidFileNameChars()));
        var dumpPath = Path.Combine(Path.GetTempPath(),
            $"integration-fault-{DateTime.UtcNow:yyyyMMdd_HHmmss}-{Guid.NewGuid():N}-{safeName}.trace.log");
        await File.WriteAllTextAsync(dumpPath, Ring.Dump(), CancellationToken.None);
        await Console.Error.WriteLineAsync(
            $"[FaultTraceDump] FAILED: {testName} — trace ring dumped to: {dumpPath}");
    }
}
