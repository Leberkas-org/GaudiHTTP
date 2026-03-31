using TurboHttp.Benchmarks;

namespace TurboHttp.Tests.Benchmarks;

/// <summary>
/// Unit tests for <see cref="BenchmarkComparisonReport"/>.
/// Verifies delta calculation, indicator symbols, and markdown table formatting.
/// </summary>
public sealed class BenchmarkComparisonReportTests
{
    // -------------------------------------------------------------------------
    // Delta calculation — ComputeDelta (higher is better, e.g. throughput)
    // -------------------------------------------------------------------------

    [Fact(DisplayName = "Bench-report-001: ComputeDelta returns positive when turbo is higher")]
    public void ComputeDelta_Should_ReturnPositive_When_TurboHigherThanBaseline()
    {
        var delta = BenchmarkComparisonReport.ComputeDelta(100.0, 120.0);
        Assert.Equal(20.0, delta, precision: 6);
    }

    [Fact(DisplayName = "Bench-report-002: ComputeDelta returns negative when turbo is lower")]
    public void ComputeDelta_Should_ReturnNegative_When_TurboLowerThanBaseline()
    {
        var delta = BenchmarkComparisonReport.ComputeDelta(100.0, 80.0);
        Assert.Equal(-20.0, delta, precision: 6);
    }

    [Fact(DisplayName = "Bench-report-003: ComputeDelta returns zero when baseline is zero")]
    public void ComputeDelta_Should_ReturnZero_When_BaselineIsZero()
    {
        var delta = BenchmarkComparisonReport.ComputeDelta(0.0, 50.0);
        Assert.Equal(0.0, delta, precision: 6);
    }

    [Fact(DisplayName = "Bench-report-004: ComputeDelta returns zero when values are equal")]
    public void ComputeDelta_Should_ReturnZero_When_ValuesAreEqual()
    {
        var delta = BenchmarkComparisonReport.ComputeDelta(100.0, 100.0);
        Assert.Equal(0.0, delta, precision: 6);
    }

    // -------------------------------------------------------------------------
    // Delta calculation — ComputeLatencyDelta (lower is better, e.g. latency/memory)
    // -------------------------------------------------------------------------

    [Fact(DisplayName = "Bench-report-005: ComputeLatencyDelta returns positive when turbo is lower")]
    public void ComputeLatencyDelta_Should_ReturnPositive_When_TurboLowerThanBaseline()
    {
        var delta = BenchmarkComparisonReport.ComputeLatencyDelta(100.0, 80.0);
        Assert.Equal(20.0, delta, precision: 6);
    }

    [Fact(DisplayName = "Bench-report-006: ComputeLatencyDelta returns negative when turbo is higher")]
    public void ComputeLatencyDelta_Should_ReturnNegative_When_TurboHigherThanBaseline()
    {
        var delta = BenchmarkComparisonReport.ComputeLatencyDelta(100.0, 120.0);
        Assert.Equal(-20.0, delta, precision: 6);
    }

    [Fact(DisplayName = "Bench-report-007: ComputeLatencyDelta returns zero when baseline is zero")]
    public void ComputeLatencyDelta_Should_ReturnZero_When_BaselineIsZero()
    {
        var delta = BenchmarkComparisonReport.ComputeLatencyDelta(0.0, 50.0);
        Assert.Equal(0.0, delta, precision: 6);
    }

    // -------------------------------------------------------------------------
    // Indicator symbols
    // -------------------------------------------------------------------------

    [Theory(DisplayName = "Bench-report-008: ThroughputIndicator returns ✓ when delta > 5%")]
    [InlineData(5.1)]
    [InlineData(10.0)]
    [InlineData(100.0)]
    public void ThroughputIndicator_Should_ReturnGreen_When_DeltaAboveThreshold(double delta)
    {
        Assert.Equal("✓", BenchmarkComparisonReport.ThroughputIndicator(delta));
    }

    [Theory(DisplayName = "Bench-report-009: ThroughputIndicator returns ✗ when delta < -5%")]
    [InlineData(-5.1)]
    [InlineData(-10.0)]
    [InlineData(-100.0)]
    public void ThroughputIndicator_Should_ReturnRed_When_DeltaBelowNegativeThreshold(double delta)
    {
        Assert.Equal("✗", BenchmarkComparisonReport.ThroughputIndicator(delta));
    }

    [Theory(DisplayName = "Bench-report-010: ThroughputIndicator returns – when delta within ±5%")]
    [InlineData(0.0)]
    [InlineData(5.0)]
    [InlineData(-5.0)]
    [InlineData(3.5)]
    [InlineData(-3.5)]
    public void ThroughputIndicator_Should_ReturnNeutral_When_DeltaWithinThreshold(double delta)
    {
        Assert.Equal("–", BenchmarkComparisonReport.ThroughputIndicator(delta));
    }

    // -------------------------------------------------------------------------
    // NsToRps conversion
    // -------------------------------------------------------------------------

    [Fact(DisplayName = "Bench-report-011: NsToRps converts 1 000 000 ns/op to 1000 req/sec")]
    public void NsToRps_Should_Convert_OneMillionNs_To_OneThousandRps()
    {
        var rps = BenchmarkComparisonReport.NsToRps(1_000_000.0);
        Assert.Equal(1000.0, rps, precision: 3);
    }

    [Fact(DisplayName = "Bench-report-012: NsToRps returns zero when mean is zero")]
    public void NsToRps_Should_ReturnZero_When_MeanIsZero()
    {
        var rps = BenchmarkComparisonReport.NsToRps(0.0);
        Assert.Equal(0.0, rps, precision: 6);
    }

    // -------------------------------------------------------------------------
    // GenerateReport — structure and formatting
    // -------------------------------------------------------------------------

    private static BenchmarkResult MakeResult(string name, double meanNs = 1_000_000, long allocBytes = 1024) =>
        new(name, meanNs, meanNs * 0.9, meanNs * 1.1, meanNs * 1.2, allocBytes);

    [Fact(DisplayName = "Bench-report-013: GenerateReport includes report title")]
    public void GenerateReport_Should_ContainTitle()
    {
        var http = new[] { MakeResult("SingleRequest_Light") };
        var turbo = new[] { MakeResult("SingleRequest_Light") };

        var report = BenchmarkComparisonReport.GenerateReport(http, turbo);

        Assert.Contains("TurboHttp vs HttpClient", report);
    }

    [Fact(DisplayName = "Bench-report-014: GenerateReport contains Throughput section header")]
    public void GenerateReport_Should_ContainThroughputSection()
    {
        var http = new[] { MakeResult("SingleRequest_Light") };
        var turbo = new[] { MakeResult("SingleRequest_Light") };

        var report = BenchmarkComparisonReport.GenerateReport(http, turbo);

        Assert.Contains("## Throughput", report);
    }

    [Fact(DisplayName = "Bench-report-015: GenerateReport contains Latency section header")]
    public void GenerateReport_Should_ContainLatencySection()
    {
        var http = new[] { MakeResult("SingleRequest_Light") };
        var turbo = new[] { MakeResult("SingleRequest_Light") };

        var report = BenchmarkComparisonReport.GenerateReport(http, turbo);

        Assert.Contains("## Latency", report);
    }

    [Fact(DisplayName = "Bench-report-016: GenerateReport contains Memory section header")]
    public void GenerateReport_Should_ContainMemorySection()
    {
        var http = new[] { MakeResult("SingleRequest_Light") };
        var turbo = new[] { MakeResult("SingleRequest_Light") };

        var report = BenchmarkComparisonReport.GenerateReport(http, turbo);

        Assert.Contains("## Memory", report);
    }

    [Fact(DisplayName = "Bench-report-017: GenerateReport contains Notes section")]
    public void GenerateReport_Should_ContainNotesSection()
    {
        var http = new[] { MakeResult("SingleRequest_Light") };
        var turbo = new[] { MakeResult("SingleRequest_Light") };

        var report = BenchmarkComparisonReport.GenerateReport(http, turbo);

        Assert.Contains("## Notes", report);
    }

    [Fact(DisplayName = "Bench-report-018: GenerateReport includes benchmark scenario name in table rows")]
    public void GenerateReport_Should_IncludeScenarioName_In_TableRow()
    {
        var http = new[] { MakeResult("ConcurrentRequests_Heavy") };
        var turbo = new[] { MakeResult("ConcurrentRequests_Heavy") };

        var report = BenchmarkComparisonReport.GenerateReport(http, turbo);

        Assert.Contains("ConcurrentRequests_Heavy", report);
    }

    [Fact(DisplayName = "Bench-report-019: GenerateReport shows ✓ indicator when turbo throughput is >5% better")]
    public void GenerateReport_Should_ShowGreenIndicator_When_TurboIsFaster()
    {
        // turbo mean = 500k ns → 2000 rps; http mean = 1M ns → 1000 rps → delta = +100%
        var http = new[] { MakeResult("SingleRequest_Light", meanNs: 1_000_000) };
        var turbo = new[] { MakeResult("SingleRequest_Light", meanNs: 500_000) };

        var report = BenchmarkComparisonReport.GenerateReport(http, turbo);

        Assert.Contains("✓", report);
    }

    [Fact(DisplayName = "Bench-report-020: GenerateReport shows ✗ indicator when turbo throughput is >5% worse")]
    public void GenerateReport_Should_ShowRedIndicator_When_TurboIsSlower()
    {
        // turbo mean = 2M ns → 500 rps; http mean = 1M ns → 1000 rps → delta = -50%
        var http = new[] { MakeResult("SingleRequest_Light", meanNs: 1_000_000) };
        var turbo = new[] { MakeResult("SingleRequest_Light", meanNs: 2_000_000) };

        var report = BenchmarkComparisonReport.GenerateReport(http, turbo);

        Assert.Contains("✗", report);
    }

    [Fact(DisplayName = "Bench-report-021: GenerateReport shows – indicator when throughput delta is within ±5%")]
    public void GenerateReport_Should_ShowNeutralIndicator_When_TurboIsWithinThreshold()
    {
        // Nearly identical mean → delta ≈ 0%
        var http = new[] { MakeResult("SingleRequest_Light", meanNs: 1_000_000) };
        var turbo = new[] { MakeResult("SingleRequest_Light", meanNs: 1_010_000) };

        var report = BenchmarkComparisonReport.GenerateReport(http, turbo);

        Assert.Contains("–", report);
    }

    [Fact(DisplayName = "Bench-report-022: GenerateReport includes table column headers")]
    public void GenerateReport_Should_ContainColumnHeaders()
    {
        var http = new[] { MakeResult("SingleRequest_Light") };
        var turbo = new[] { MakeResult("SingleRequest_Light") };

        var report = BenchmarkComparisonReport.GenerateReport(http, turbo);

        Assert.Contains("HttpClient", report);
        Assert.Contains("TurboHttp", report);
        Assert.Contains("Delta%", report);
    }

    [Fact(DisplayName = "Bench-report-023: GenerateReport handles empty result lists gracefully")]
    public void GenerateReport_Should_NotThrow_When_ResultListsAreEmpty()
    {
        var report = BenchmarkComparisonReport.GenerateReport([], []);

        Assert.NotEmpty(report);
        Assert.Contains("## Throughput", report);
    }

    [Fact(DisplayName = "Bench-report-024: GenerateReport handles mismatched scenario names")]
    public void GenerateReport_Should_IncludeHttpOnlyRows_When_TurboHasNoMatch()
    {
        var http = new[]
        {
            MakeResult("ScenarioA"),
            MakeResult("ScenarioB")
        };
        var turbo = new[] { MakeResult("ScenarioA") };

        var report = BenchmarkComparisonReport.GenerateReport(http, turbo);

        // ScenarioA matched; ScenarioB has no turbo counterpart and should still appear
        Assert.Contains("ScenarioA", report);
        Assert.Contains("ScenarioB", report);
    }

    [Fact(DisplayName = "Bench-report-025: GenerateReport includes p50/p95/p99 subsection headers")]
    public void GenerateReport_Should_ContainLatencyPercentileSubsections()
    {
        var http = new[] { MakeResult("SingleRequest_Light") };
        var turbo = new[] { MakeResult("SingleRequest_Light") };

        var report = BenchmarkComparisonReport.GenerateReport(http, turbo);

        Assert.Contains("p50", report);
        Assert.Contains("p95", report);
        Assert.Contains("p99", report);
    }

    // -------------------------------------------------------------------------
    // WriteReportToFile
    // -------------------------------------------------------------------------

    [Fact(DisplayName = "Bench-report-026: WriteReportToFile creates file under benchmarks/ directory")]
    public void WriteReportToFile_Should_CreateFileUnderBenchmarksDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"turbohttp-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var originalCwd = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(tempDir);

            try
            {
                var filePath = BenchmarkComparisonReport.WriteReportToFile("# Test");

                Assert.True(File.Exists(filePath));
                Assert.Contains("benchmarks", filePath);
                Assert.Contains("comparison_report_", filePath);
                Assert.EndsWith(".md", filePath);
            }
            finally
            {
                Directory.SetCurrentDirectory(originalCwd);
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact(DisplayName = "Bench-report-027: WriteReportToFile writes the provided markdown content")]
    public void WriteReportToFile_Should_WriteMarkdownContentToFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"turbohttp-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var originalCwd = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(tempDir);

            try
            {
                const string expected = "# My Report\n\nSome content.";
                var filePath = BenchmarkComparisonReport.WriteReportToFile(expected);

                var actual = File.ReadAllText(filePath);
                Assert.Equal(expected, actual);
            }
            finally
            {
                Directory.SetCurrentDirectory(originalCwd);
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
