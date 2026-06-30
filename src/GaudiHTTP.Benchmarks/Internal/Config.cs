using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace GaudiHTTP.Benchmarks.Internal;

public class RequestsPerSecondColumn : IColumn
{
    public string Id => nameof(RequestsPerSecondColumn);
    public string ColumnName => "Req/sec";

    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase) =>
        GetValue(summary, benchmarkCase, SummaryStyle.Default);

    public bool IsAvailable(Summary summary) => true;
    public bool AlwaysShow => true;
    public ColumnCategory Category => ColumnCategory.Custom;
    public int PriorityInCategory => -1;
    public bool IsNumeric => true;
    public UnitType UnitType => UnitType.Dimensionless;
    public string Legend => "Requests per Second";

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style)
    {
        if (!summary.HasReport(benchmarkCase))
        {
            return "<not found>";
        }

        var report = summary[benchmarkCase];
        var statistics = report?.ResultStatistics;
        if (statistics is null)
        {
            return "<not found>";
        }

        // For concurrent benchmarks, each invocation fires ConcurrencyLevel requests
        // simultaneously. The Mean measures one invocation, so multiply to get actual req/s.
        var isConcurrent = benchmarkCase.Descriptor.WorkloadMethod.Name.Contains("Concurrent",
            StringComparison.Ordinal);
        var concurrencyLevel = isConcurrent
                               && benchmarkCase.Parameters.Items.FirstOrDefault(p => p.Name == "ConcurrencyLevel")
                                   ?.Value is int cl
            ? cl
            : 1;

        var invocationsPerSecond = 1.0 / (statistics.Mean / 1e9);
        var requestsPerSecond = invocationsPerSecond * concurrencyLevel;

        return requestsPerSecond.ToString("N2");
    }
}

/// <summary>
/// Re-emits the BenchmarkDotNet summary table to the console with ANSI color codes applied
/// per HTTP version row: cyan = HTTP/1.1, green = HTTP/2.0, yellow = HTTP/3.0.
/// </summary>
public sealed class HttpVersionColorExporter : IExporter
{
    public static readonly HttpVersionColorExporter Default = new();

    public string Name => nameof(HttpVersionColorExporter);

    public IEnumerable<string> ExportToFiles(Summary summary, ILogger consoleLogger)
        => [];

    public void ExportToLog(Summary summary, ILogger logger)
    {
        var capture = new CaptureLogger();
        MarkdownExporter.GitHub.ExportToLog(summary, capture);

        foreach (var line in capture.GetLines())
        {
            var ansi = VersionAnsi(line);
            if (ansi != null)
                logger.Write(LogKind.Default, ansi + line + "\x1b[0m\n");
            else
                logger.WriteLine(LogKind.Default, line);
        }
    }

    private static string? VersionAnsi(string line)
    {
        if (line.Length == 0 || line[0] != '|')
        {
            return null;
        }

        if (line.Contains("| 1.1 |", StringComparison.Ordinal)) return "\x1b[36m"; // cyan
        if (line.Contains("| 2.0 |", StringComparison.Ordinal)) return "\x1b[32m"; // green
        if (line.Contains("| 3.0 |", StringComparison.Ordinal)) return "\x1b[33m"; // yellow
        return null;
    }

    private sealed class CaptureLogger : ILogger
    {
        private readonly System.Text.StringBuilder _sb = new();

        public string Id => nameof(CaptureLogger);
        public int Priority => 0;

        public void Write(LogKind logKind, string text) => _sb.Append(text);
        public void WriteLine(LogKind logKind, string text) => _sb.Append(text).Append('\n');
        public void WriteLine() => _sb.Append('\n');
        public void Flush() { }

        public IEnumerable<string> GetLines()
            => _sb.ToString().Split('\n').Select(l => l.TrimEnd('\r'));
    }
}

/// <summary>
/// Benchmark configuration for engine-level throughput and latency measurements.
/// Includes p50/p95/p100 latency percentile columns, memory diagnostics, and a
/// requests-per-second column for throughput visibility.
/// </summary>
public class EngineBenchmarkConfig : ManualConfig
{
    public EngineBenchmarkConfig()
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var artifactsPath = Path.Combine("BenchmarkDotNet.Artifacts", timestamp);

        WithArtifactsPath(artifactsPath);
        AddJob(Job.Default.WithGcServer(true));
        AddDiagnoser(MemoryDiagnoser.Default);
        AddDiagnoser(ThreadingDiagnoser.Default);
        AddExporter(MarkdownExporter.GitHub);
        AddExporter(HttpVersionColorExporter.Default);
        AddExporter(AllocationByTypeExporter.Default);
        AddColumn(StatisticColumn.P50);
        AddColumn(StatisticColumn.P95);
        AddColumn(StatisticColumn.P100);
        AddColumn(new RequestsPerSecondColumn());
    }
}

/// <summary>
/// Config for allocation-focused benchmarks (client streaming, against an out-of-process server).
/// Allocation is measured PROCESS-WIDE via EventPipe GCAllocationTick (sampled ~100 KB/tick) and
/// surfaced by <see cref="AllocationByTypeExporter"/> — NOT via MemoryDiagnoser, whose
/// GetAllocatedBytesForCurrentThread only sees the calling thread and so massively under-counts the
/// Akka dispatcher / Task background-thread allocations this code does. Server GC, low iteration
/// counts (allocation is deterministic), and machine-readable JSON for charting.
/// </summary>
public class AllocationBenchmarkConfig : ManualConfig
{
    public AllocationBenchmarkConfig()
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        WithArtifactsPath(Path.Combine("BenchmarkDotNet.Artifacts", string.Concat("alloc_", timestamp)));

        // Monitoring strategy with a fixed, low iteration count: each invocation is an expensive
        // concurrent batch, so the default Throughput strategy would auto-scale to thousands of runs
        // and pin every core. EventPipe profiles the actual run (no extra benchmarks run) to avoid
        // doubling that cost. Allocation is deterministic enough that a few iterations suffice.
        AddJob(Job.Default
            .WithGcServer(true)
            .WithStrategy(RunStrategy.Monitoring)
            .WithLaunchCount(1)
            .WithWarmupCount(1)
            .WithIterationCount(3));

        AddDiagnoser(new EventPipeProfiler(EventPipeProfile.GcVerbose, performExtraBenchmarksRun: false));
        AddExporter(MarkdownExporter.GitHub);
        AddExporter(JsonExporter.Full);
        AddExporter(AllocationByTypeExporter.Default);
    }
}

/// <summary>
/// Config for in-memory micro-benchmarks that still allocate on background threads (e.g. the
/// concurrent object-pool stress). Same rationale as <see cref="AllocationBenchmarkConfig"/>:
/// process-wide EventPipe allocation, not the calling-thread MemoryDiagnoser. Uses the Monitoring
/// strategy with a low fixed iteration count so a CPU-bound concurrent body is not auto-scaled into
/// minutes of 100% CPU, and profiles the actual run (no extra run).
/// </summary>
public class MicroBenchmarkConfig : ManualConfig
{
    public MicroBenchmarkConfig()
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        WithArtifactsPath(Path.Combine("BenchmarkDotNet.Artifacts", string.Concat("micro_", timestamp)));

        AddJob(Job.Default
            .WithGcServer(true)
            .WithStrategy(RunStrategy.Monitoring)
            .WithLaunchCount(1)
            .WithWarmupCount(1)
            .WithIterationCount(3));

        AddDiagnoser(new EventPipeProfiler(EventPipeProfile.GcVerbose, performExtraBenchmarksRun: false));
        AddExporter(MarkdownExporter.GitHub);
        AddExporter(JsonExporter.Full);
        AddExporter(AllocationByTypeExporter.Default);
    }
}