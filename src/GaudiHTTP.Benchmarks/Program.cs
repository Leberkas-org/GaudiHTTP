using BenchmarkDotNet.Running;
using GaudiHTTP.Benchmarks.Internal;
using GaudiHTTP.Benchmarks.Kestrel;

// Internal child-process entry used by ClientAllocationBenchmarks' GlobalSetup to run the Kestrel
// server out of process (so client allocation is measured in isolation). Not a user-facing tool.
if (args.Length > 0 && args[0] == ServerProcessHandle.ServerArgument)
{
    await ServerProcessHandle.RunServerAsync();
    return;
}

var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

var enumerable = summaries.ToList();

var kestrelHttp = enumerable.FirstOrDefault(s => s.HasBenchmarksOf<KestrelHttpClientConcurrentBenchmarks>());
var kestrelGaudiSend = enumerable.FirstOrDefault(s => s.HasBenchmarksOf<KestrelGaudiSendAsyncConcurrentBenchmarks>());
var kestrelGaudiStream = enumerable.FirstOrDefault(s => s.HasBenchmarksOf<KestrelGaudiStreamingConcurrentBenchmarks>());

if (kestrelHttp is not null
    && kestrelGaudiSend is not null
    && kestrelGaudiStream is not null)
{
    var markdown = BenchmarkComparisonReport.GenerateKestrelReport(
        SummaryExtractor.Extract(kestrelHttp),
        SummaryExtractor.Extract(kestrelGaudiSend),
        SummaryExtractor.Extract(kestrelGaudiStream));

    if (markdown.Contains("NaN") || markdown.Contains("Infinity") || markdown.Contains("Inf%"))
    {
        Console.Error.WriteLine("WARNING: Kestrel report contains NaN or Inf values — check input data.");
    }

    var path = BenchmarkComparisonReport.WriteReportToFile(markdown, "kestrel_client");
    Console.WriteLine($"Kestrel comparison report: {path}");
}
else
{
    Console.WriteLine("Kestrel comparison report skipped — not all 3 benchmark suites ran.");
    Console.WriteLine("Required Kestrel suites:");
    Console.WriteLine($"  KestrelHttpClientConcurrentBenchmarks        : {(kestrelHttp is not null ? "OK" : "MISSING")}");
    Console.WriteLine($"  KestrelGaudiSendAsyncConcurrentBenchmarks    : {(kestrelGaudiSend is not null ? "OK" : "MISSING")}");
    Console.WriteLine($"  KestrelGaudiStreamingConcurrentBenchmarks    : {(kestrelGaudiStream is not null ? "OK" : "MISSING")}");
}

var kestrelServerPlaintext = enumerable.FirstOrDefault(s =>
    s.HasBenchmarksOf<GaudiHTTP.Benchmarks.Server.Kestrel.KestrelServerPlaintextBenchmark>());
var gaudiServerPlaintext = enumerable.FirstOrDefault(s =>
    s.HasBenchmarksOf<GaudiHTTP.Benchmarks.Server.Gaudi.GaudiServerPlaintextBenchmark>());
var kestrelServerJson = enumerable.FirstOrDefault(s =>
    s.HasBenchmarksOf<GaudiHTTP.Benchmarks.Server.Kestrel.KestrelServerJsonBenchmark>());
var gaudiServerJson = enumerable.FirstOrDefault(s =>
    s.HasBenchmarksOf<GaudiHTTP.Benchmarks.Server.Gaudi.GaudiServerJsonBenchmark>());
var kestrelServerFortunes = enumerable.FirstOrDefault(s =>
    s.HasBenchmarksOf<GaudiHTTP.Benchmarks.Server.Kestrel.KestrelServerFortunesBenchmark>());
var gaudiServerFortunes = enumerable.FirstOrDefault(s =>
    s.HasBenchmarksOf<GaudiHTTP.Benchmarks.Server.Gaudi.GaudiServerFortunesBenchmark>());
var kestrelServerUpload = enumerable.FirstOrDefault(s =>
    s.HasBenchmarksOf<GaudiHTTP.Benchmarks.Server.Kestrel.KestrelServerUploadBenchmark>());
var gaudiServerUpload = enumerable.FirstOrDefault(s =>
    s.HasBenchmarksOf<GaudiHTTP.Benchmarks.Server.Gaudi.GaudiServerUploadBenchmark>());

var hasAnyServerBenchmarks =
    kestrelServerPlaintext is not null || gaudiServerPlaintext is not null ||
    kestrelServerJson is not null || gaudiServerJson is not null ||
    kestrelServerFortunes is not null || gaudiServerFortunes is not null ||
    kestrelServerUpload is not null || gaudiServerUpload is not null;

if (hasAnyServerBenchmarks)
{
    var kestrelServerResults = new List<BenchmarkResult>();
    var gaudiServerResults = new List<BenchmarkResult>();

    if (kestrelServerPlaintext is not null) kestrelServerResults.AddRange(SummaryExtractor.Extract(kestrelServerPlaintext));
    if (kestrelServerJson is not null) kestrelServerResults.AddRange(SummaryExtractor.Extract(kestrelServerJson));
    if (kestrelServerFortunes is not null) kestrelServerResults.AddRange(SummaryExtractor.Extract(kestrelServerFortunes));
    if (kestrelServerUpload is not null) kestrelServerResults.AddRange(SummaryExtractor.Extract(kestrelServerUpload));

    if (gaudiServerPlaintext is not null) gaudiServerResults.AddRange(SummaryExtractor.Extract(gaudiServerPlaintext));
    if (gaudiServerJson is not null) gaudiServerResults.AddRange(SummaryExtractor.Extract(gaudiServerJson));
    if (gaudiServerFortunes is not null) gaudiServerResults.AddRange(SummaryExtractor.Extract(gaudiServerFortunes));
    if (gaudiServerUpload is not null) gaudiServerResults.AddRange(SummaryExtractor.Extract(gaudiServerUpload));

    var serverMarkdown = BenchmarkComparisonReport.GenerateServerReport(kestrelServerResults, gaudiServerResults);

    if (serverMarkdown.Contains("NaN") || serverMarkdown.Contains("Infinity") || serverMarkdown.Contains("Inf%"))
    {
        Console.Error.WriteLine("WARNING: Server report contains NaN or Inf values — check input data.");
    }

    var serverPath = BenchmarkComparisonReport.WriteReportToFile(serverMarkdown, "server");
    Console.WriteLine($"Server comparison report: {serverPath}");
}
else
{
    Console.WriteLine("Server comparison report skipped — no server benchmark suites ran.");
}
