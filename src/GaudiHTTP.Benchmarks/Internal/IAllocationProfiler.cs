namespace TurboHTTP.Benchmarks.Internal;

/// <summary>
/// Abstraction for in-process per-type allocation profiling via GCAllocationTick.
/// </summary>
public interface IAllocationProfiler
{
    void Arm();
    void Disarm();
    void Reset();
    string ReportText(int top = 25);
    void Report(long totalRequests, int top = 20);
}
