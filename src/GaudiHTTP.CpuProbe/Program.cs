using System.Diagnostics;

namespace GaudiHTTP.CpuProbe;

internal static class Program
{
    private const int TotalRequestsPerCell = 200_000;
    private static readonly int[] Concurrencies = [1, 64, 512];

    private static async Task Main()
    {
        // Reuse GaudiHTTP.Benchmarks' construction here:
        // 1) start an in-proc Kestrel with an h2c (cleartext HTTP/2) + HTTP/1.1 listener
        //    returning a fixed 1 KiB body for GET /probe;
        // 2) build a GaudiHttpClient with MaxConnectionsPerServer = 16 and
        //    MaxConcurrentStreams = 512.
        await using var fixture = await ProbeFixture.StartAsync();

        foreach (var protocol in new[] { "h1.1", "h2" })
        {
            foreach (var cl in Concurrencies)
            {
                var result = await RunCellAsync(fixture, protocol, cl, CancellationToken.None);
                Console.WriteLine(
                    "{0,-5} CL={1,-4} rps={2,10:N0} cpu%={3,6:F1} peakThreads={4,4} allocMB={5,8:F1}",
                    protocol, cl, result.Rps, result.CpuPercent, result.PeakThreads,
                    result.AllocBytes / (1024.0 * 1024.0));
            }
        }
    }

    private static async Task<CellResult> RunCellAsync(
        ProbeFixture fixture, string protocol, int concurrency, CancellationToken ct)
    {
        var perWorker = TotalRequestsPerCell / concurrency;
        var proc = Process.GetCurrentProcess();
        var cores = Environment.ProcessorCount;

        // Warmup
        await Task.WhenAll(Enumerable.Range(0, concurrency)
            .Select(_ => fixture.HammerAsync(protocol, Math.Min(perWorker, 1_000), ct)));

        var cpuBefore = proc.TotalProcessorTime;
        var sw = Stopwatch.StartNew();
        var peakThreads = 0;
        using var monitor = new Timer(_ =>
        {
            var t = Process.GetCurrentProcess().Threads.Count;
            if (t > peakThreads) { peakThreads = t; }
        }, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(50));

        var allocBefore = GC.GetTotalAllocatedBytes(precise: false);
        await Task.WhenAll(Enumerable.Range(0, concurrency)
            .Select(_ => fixture.HammerAsync(protocol, perWorker, ct)));
        var allocDelta = GC.GetTotalAllocatedBytes(precise: false) - allocBefore;

        sw.Stop();
        var cpuDelta = (proc.TotalProcessorTime - cpuBefore).TotalSeconds;
        var totalDone = perWorker * concurrency;

        return new CellResult(
            Rps: totalDone / sw.Elapsed.TotalSeconds,
            CpuPercent: cpuDelta / sw.Elapsed.TotalSeconds / cores * 100.0,
            PeakThreads: peakThreads,
            AllocBytes: allocDelta);
    }

    private readonly record struct CellResult(double Rps, double CpuPercent, int PeakThreads, long AllocBytes);
}
