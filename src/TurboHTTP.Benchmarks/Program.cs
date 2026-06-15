using BenchmarkDotNet.Running;
using TurboHTTP.Benchmarks.Internal;
using TurboHTTP.Benchmarks.Kestrel;
using TurboHTTP.Benchmarks.LoadTest;

// Open-loop sustained-RPS mode: `dotnet run -c Release -- loadtest [--connections N --pipeline N ...]`.
// Separate from the BenchmarkDotNet (closed-loop) suites below.
if (args.Length > 0 && args[0].Equals("loadtest", StringComparison.OrdinalIgnoreCase))
{
    var loadOptions = LoadTestOptions.Parse(args);

    // Child server mode: host one server in this process, print its H1.1 port, run until killed.
    if (loadOptions.Serve is { } serveKind)
    {
        await LoadTestServerHost.RunAsync(serveKind);
        return;
    }

    if (loadOptions.Protocol.StartsWith("mem-", StringComparison.Ordinal))
    {
        InMemoryBenchmark.Run(loadOptions);
    }
    else
    {
        await OpenLoopLoadTest.RunAsync(loadOptions);
    }

    return;
}

var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

var enumerable = summaries.ToList();

var kestrelHttp = enumerable.FirstOrDefault(s => s.HasBenchmarksOf<KestrelHttpClientConcurrentBenchmarks>());
var kestrelTurboSend = enumerable.FirstOrDefault(s => s.HasBenchmarksOf<KestrelTurboSendAsyncConcurrentBenchmarks>());
var kestrelTurboStream = enumerable.FirstOrDefault(s => s.HasBenchmarksOf<KestrelTurboStreamingConcurrentBenchmarks>());

if (kestrelHttp is not null
    && kestrelTurboSend is not null
    && kestrelTurboStream is not null)
{
    var markdown = BenchmarkComparisonReport.GenerateKestrelReport(
        SummaryExtractor.Extract(kestrelHttp),
        SummaryExtractor.Extract(kestrelTurboSend),
        SummaryExtractor.Extract(kestrelTurboStream));

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
    Console.WriteLine($"  KestrelTurboSendAsyncConcurrentBenchmarks    : {(kestrelTurboSend is not null ? "OK" : "MISSING")}");
    Console.WriteLine($"  KestrelTurboStreamingConcurrentBenchmarks    : {(kestrelTurboStream is not null ? "OK" : "MISSING")}");
}

var kestrelServerPlaintext = enumerable.FirstOrDefault(s =>
    s.HasBenchmarksOf<TurboHTTP.Benchmarks.Server.Kestrel.KestrelServerPlaintextBenchmark>());
var turboServerPlaintext = enumerable.FirstOrDefault(s =>
    s.HasBenchmarksOf<TurboHTTP.Benchmarks.Server.Turbo.TurboServerPlaintextBenchmark>());
var kestrelServerJson = enumerable.FirstOrDefault(s =>
    s.HasBenchmarksOf<TurboHTTP.Benchmarks.Server.Kestrel.KestrelServerJsonBenchmark>());
var turboServerJson = enumerable.FirstOrDefault(s =>
    s.HasBenchmarksOf<TurboHTTP.Benchmarks.Server.Turbo.TurboServerJsonBenchmark>());
var kestrelServerFortunes = enumerable.FirstOrDefault(s =>
    s.HasBenchmarksOf<TurboHTTP.Benchmarks.Server.Kestrel.KestrelServerFortunesBenchmark>());
var turboServerFortunes = enumerable.FirstOrDefault(s =>
    s.HasBenchmarksOf<TurboHTTP.Benchmarks.Server.Turbo.TurboServerFortunesBenchmark>());
var kestrelServerUpload = enumerable.FirstOrDefault(s =>
    s.HasBenchmarksOf<TurboHTTP.Benchmarks.Server.Kestrel.KestrelServerUploadBenchmark>());
var turboServerUpload = enumerable.FirstOrDefault(s =>
    s.HasBenchmarksOf<TurboHTTP.Benchmarks.Server.Turbo.TurboServerUploadBenchmark>());

var hasAnyServerBenchmarks =
    kestrelServerPlaintext is not null || turboServerPlaintext is not null ||
    kestrelServerJson is not null || turboServerJson is not null ||
    kestrelServerFortunes is not null || turboServerFortunes is not null ||
    kestrelServerUpload is not null || turboServerUpload is not null;

if (hasAnyServerBenchmarks)
{
    var kestrelServerResults = new List<BenchmarkResult>();
    var turboServerResults = new List<BenchmarkResult>();

    if (kestrelServerPlaintext is not null) kestrelServerResults.AddRange(SummaryExtractor.Extract(kestrelServerPlaintext));
    if (kestrelServerJson is not null) kestrelServerResults.AddRange(SummaryExtractor.Extract(kestrelServerJson));
    if (kestrelServerFortunes is not null) kestrelServerResults.AddRange(SummaryExtractor.Extract(kestrelServerFortunes));
    if (kestrelServerUpload is not null) kestrelServerResults.AddRange(SummaryExtractor.Extract(kestrelServerUpload));

    if (turboServerPlaintext is not null) turboServerResults.AddRange(SummaryExtractor.Extract(turboServerPlaintext));
    if (turboServerJson is not null) turboServerResults.AddRange(SummaryExtractor.Extract(turboServerJson));
    if (turboServerFortunes is not null) turboServerResults.AddRange(SummaryExtractor.Extract(turboServerFortunes));
    if (turboServerUpload is not null) turboServerResults.AddRange(SummaryExtractor.Extract(turboServerUpload));

    var serverMarkdown = BenchmarkComparisonReport.GenerateServerReport(kestrelServerResults, turboServerResults);

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
