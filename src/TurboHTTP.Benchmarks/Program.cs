using BenchmarkDotNet.Running;
using TurboHTTP.Benchmarks.Binkraken;
using TurboHTTP.Benchmarks.Internal;
using TurboHTTP.Benchmarks.Kestrel;

var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

var enumerable = summaries.ToList();

var binkHttpSingle = enumerable.FirstOrDefault(s => s.HasBenchmarksOf<BinkrakenHttpClientSingleBenchmarks>());
var binkHttpConcurrent = enumerable.FirstOrDefault(s => s.HasBenchmarksOf<BinkrakenHttpClientConcurrentBenchmarks>());
var binkTurboSendSingle = enumerable.FirstOrDefault(s => s.HasBenchmarksOf<BinkrakenTurboSendAsyncSingleBenchmarks>());
var binkTurboSendConcurrent = enumerable.FirstOrDefault(s => s.HasBenchmarksOf<BinkrakenTurboSendAsyncConcurrentBenchmarks>());
var binkTurboStreamSingle = enumerable.FirstOrDefault(s => s.HasBenchmarksOf<BinkrakenTurboStreamingSingleBenchmarks>());
var binkTurboStreamConcurrent = enumerable.FirstOrDefault(s => s.HasBenchmarksOf<BinkrakenTurboStreamingConcurrentBenchmarks>());

if (binkHttpSingle is not null
    && binkHttpConcurrent is not null
    && binkTurboSendSingle is not null
    && binkTurboSendConcurrent is not null
    && binkTurboStreamSingle is not null
    && binkTurboStreamConcurrent is not null)
{
    var markdown = BenchmarkComparisonReport.GenerateReport(
        SummaryExtractor.Extract(binkHttpSingle),
        SummaryExtractor.Extract(binkTurboSendSingle),
        SummaryExtractor.Extract(binkTurboStreamSingle),
        SummaryExtractor.Extract(binkHttpConcurrent),
        SummaryExtractor.Extract(binkTurboSendConcurrent),
        SummaryExtractor.Extract(binkTurboStreamConcurrent));

    if (markdown.Contains("NaN") || markdown.Contains("Infinity") || markdown.Contains("Inf%"))
    {
        Console.Error.WriteLine("WARNING: Binkraken report contains NaN or Inf values — check input data.");
    }

    var path = BenchmarkComparisonReport.WriteReportToFile(markdown);
    Console.WriteLine($"Binkraken comparison report: {path}");
}
else
{
    Console.WriteLine("Binkraken comparison report skipped — not all 6 benchmark suites ran.");
    Console.WriteLine("Required Binkraken suites:");
    Console.WriteLine($"  BinkrakenHttpClientSingleBenchmarks          : {(binkHttpSingle is not null ? "OK" : "MISSING")}");
    Console.WriteLine($"  BinkrakenHttpClientConcurrentBenchmarks      : {(binkHttpConcurrent is not null ? "OK" : "MISSING")}");
    Console.WriteLine($"  BinkrakenTurboSendAsyncSingleBenchmarks      : {(binkTurboSendSingle is not null ? "OK" : "MISSING")}");
    Console.WriteLine($"  BinkrakenTurboSendAsyncConcurrentBenchmarks  : {(binkTurboSendConcurrent is not null ? "OK" : "MISSING")}");
    Console.WriteLine($"  BinkrakenTurboStreamingSingleBenchmarks      : {(binkTurboStreamSingle is not null ? "OK" : "MISSING")}");
    Console.WriteLine($"  BinkrakenTurboStreamingConcurrentBenchmarks  : {(binkTurboStreamConcurrent is not null ? "OK" : "MISSING")}");
}

var kestrelHttpSingle = enumerable.FirstOrDefault(s => s.HasBenchmarksOf<KestrelHttpClientSingleBenchmarks>());
var kestrelHttpConcurrent = enumerable.FirstOrDefault(s => s.HasBenchmarksOf<KestrelHttpClientConcurrentBenchmarks>());
var kestrelTurboSendSingle = enumerable.FirstOrDefault(s => s.HasBenchmarksOf<KestrelTurboSendAsyncSingleBenchmarks>());
var kestrelTurboSendConcurrent = enumerable.FirstOrDefault(s => s.HasBenchmarksOf<KestrelTurboSendAsyncConcurrentBenchmarks>());
var kestrelTurboStreamSingle = enumerable.FirstOrDefault(s => s.HasBenchmarksOf<KestrelTurboStreamingSingleBenchmarks>());
var kestrelTurboStreamConcurrent = enumerable.FirstOrDefault(s => s.HasBenchmarksOf<KestrelTurboStreamingConcurrentBenchmarks>());

if (kestrelHttpSingle is not null
    && kestrelHttpConcurrent is not null
    && kestrelTurboSendSingle is not null
    && kestrelTurboSendConcurrent is not null
    && kestrelTurboStreamSingle is not null
    && kestrelTurboStreamConcurrent is not null)
{
    var markdown = BenchmarkComparisonReport.GenerateKestrelReport(
        SummaryExtractor.Extract(kestrelHttpSingle),
        SummaryExtractor.Extract(kestrelTurboSendSingle),
        SummaryExtractor.Extract(kestrelTurboStreamSingle),
        SummaryExtractor.Extract(kestrelHttpConcurrent),
        SummaryExtractor.Extract(kestrelTurboSendConcurrent),
        SummaryExtractor.Extract(kestrelTurboStreamConcurrent));

    if (markdown.Contains("NaN") || markdown.Contains("Infinity") || markdown.Contains("Inf%"))
    {
        Console.Error.WriteLine("WARNING: Kestrel report contains NaN or Inf values — check input data.");
    }

    var path = BenchmarkComparisonReport.WriteReportToFile(markdown);
    Console.WriteLine($"Kestrel comparison report: {path}");
}
else
{
    Console.WriteLine("Kestrel comparison report skipped — not all 6 benchmark suites ran.");
    Console.WriteLine("Required Kestrel suites:");
    Console.WriteLine($"  KestrelHttpClientSingleBenchmarks            : {(kestrelHttpSingle is not null ? "OK" : "MISSING")}");
    Console.WriteLine($"  KestrelHttpClientConcurrentBenchmarks        : {(kestrelHttpConcurrent is not null ? "OK" : "MISSING")}");
    Console.WriteLine($"  KestrelTurboSendAsyncSingleBenchmarks        : {(kestrelTurboSendSingle is not null ? "OK" : "MISSING")}");
    Console.WriteLine($"  KestrelTurboSendAsyncConcurrentBenchmarks    : {(kestrelTurboSendConcurrent is not null ? "OK" : "MISSING")}");
    Console.WriteLine($"  KestrelTurboStreamingSingleBenchmarks        : {(kestrelTurboStreamSingle is not null ? "OK" : "MISSING")}");
    Console.WriteLine($"  KestrelTurboStreamingConcurrentBenchmarks    : {(kestrelTurboStreamConcurrent is not null ? "OK" : "MISSING")}");
}