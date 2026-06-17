using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;

namespace TurboHTTP.Benchmarks.Internal;

/// <summary>
/// Post-processes EventPipeProfiler .nettrace files to extract per-type allocation
/// breakdown from GCAllocationTick events. Prints a ranked table to the console and
/// writes a per-benchmark .md file to the artifacts directory.
/// </summary>
public sealed class AllocationByTypeExporter : IExporter
{
    public static readonly AllocationByTypeExporter Default = new();

    public string Name => nameof(AllocationByTypeExporter);

    public void ExportToLog(Summary summary, ILogger logger)
    {
    }

    public IEnumerable<string> ExportToFiles(Summary summary, ILogger consoleLogger)
    {
        var artifactsPath = summary.ResultsDirectoryPath;
        if (artifactsPath is null)
        {
            return [];
        }

        var traceDir = Path.GetDirectoryName(artifactsPath);
        if (traceDir is null || !Directory.Exists(traceDir))
        {
            return [];
        }

        var traceFiles = Directory.GetFiles(traceDir, "*.nettrace", SearchOption.TopDirectoryOnly);
        if (traceFiles.Length == 0)
        {
            return [];
        }

        var outputFiles = new List<string>();

        consoleLogger.WriteLine();
        consoleLogger.WriteLine(LogKind.Header,
            string.Concat("// * Allocation By Type (", traceFiles.Length.ToString(), " GcVerbose traces) *"));

        foreach (var traceFile in traceFiles.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var allocs = ParseAllocations(traceFile);
            if (allocs.Count == 0)
            {
                continue;
            }

            var benchmarkName = Path.GetFileNameWithoutExtension(traceFile);
            var dashIdx = benchmarkName.LastIndexOf('-');
            if (dashIdx > 0)
            {
                benchmarkName = benchmarkName[..dashIdx];
            }

            consoleLogger.WriteLine();
            consoleLogger.WriteLine(LogKind.Header, string.Concat("  --- ", benchmarkName, " ---"));
            consoleLogger.WriteLine(LogKind.Default, $"  {"Type",-62} {"~Bytes",12} {"Ticks",8}");
            consoleLogger.WriteLine(LogKind.Default, "  " + new string('-', 84));

            foreach (var (type, bytes, count) in allocs.Take(20))
            {
                var shortType = type.Length > 60
                    ? string.Concat("…", type.AsSpan(type.Length - 59))
                    : type;
                consoleLogger.WriteLine(LogKind.Default, $"  {shortType,-62} {bytes,12:N0} {count,8:N0}");
            }

            var mdPath = Path.ChangeExtension(traceFile, ".alloc-by-type.md");
            using var writer = new StreamWriter(mdPath);
            writer.WriteLine("# Allocation By Type");
            writer.WriteLine();
            writer.WriteLine($"| {"Type",-60} | {"~Bytes",12} | {"Ticks",8} |");
            writer.WriteLine($"|{new string('-', 62)}|{new string('-', 14)}|{new string('-', 10)}|");

            foreach (var (type, bytes, count) in allocs.Take(30))
            {
                var shortType = type.Length > 58
                    ? string.Concat("…", type.AsSpan(type.Length - 57))
                    : type;
                writer.WriteLine($"| {shortType,-60} | {bytes,12:N0} | {count,8:N0} |");
            }

            outputFiles.Add(mdPath);
        }

        foreach (var traceFile in traceFiles)
        {
            TryDelete(traceFile);
            TryDelete(Path.ChangeExtension(traceFile, ".speedscope.json"));
        }

        return outputFiles;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static List<(string Type, long Bytes, long Count)> ParseAllocations(string traceFile)
    {
        var allocs = new Dictionary<string, (long Bytes, long Count)>();

        try
        {
            using var source = new EventPipeEventSource(traceFile);
            source.Clr.GCAllocationTick += (GCAllocationTickTraceData data) =>
            {
                var type = data.TypeName;
                if (type is null)
                {
                    return;
                }

                var current = allocs.GetValueOrDefault(type);
                allocs[type] = (current.Bytes + data.AllocationAmount64, current.Count + 1);
            };
            source.Process();
        }
        catch
        {
            return [];
        }

        return allocs
            .OrderByDescending(kv => kv.Value.Bytes)
            .Select(kv => (kv.Key, kv.Value.Bytes, kv.Value.Count))
            .ToList();
    }
}
