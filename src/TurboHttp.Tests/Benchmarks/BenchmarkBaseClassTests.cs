using TurboHttp.Benchmarks;

namespace TurboHttp.Tests.Benchmarks;

/// <summary>
/// Unit tests for BenchmarkBaseClass helper utilities.
/// Verifies determinism and correctness of payload generation and URI helpers.
/// </summary>
public sealed class BenchmarkBaseClassTests
{
    // Concrete subclass so we can test instance methods on an abstract base.
    private sealed class ConcreteBenchmark : BenchmarkBaseClass
    {
        public ConcreteBenchmark(int port) => KestrelPort = port;
    }

    [Fact(DisplayName = "Bench-payload-001: GeneratePayload returns identical bytes on repeated calls for same size")]
    public void Should_ReturnDeterministicBytes_When_CalledTwiceWithSameSize()
    {
        var first = BenchmarkBaseClass.GeneratePayload(1024);
        var second = BenchmarkBaseClass.GeneratePayload(1024);
        Assert.Equal(first, second);
    }

    [Fact(DisplayName = "Bench-payload-002: GeneratePayload returns array of requested length")]
    public void Should_ReturnCorrectLength_When_GeneratingPayload()
    {
        var payload = BenchmarkBaseClass.GeneratePayload(100);
        Assert.Equal(100, payload.Length);
    }

    [Fact(DisplayName = "Bench-payload-003: GeneratePayload with zero bytes returns empty array")]
    public void Should_ReturnEmptyArray_When_SizeIsZero()
    {
        var payload = BenchmarkBaseClass.GeneratePayload(0);
        Assert.Empty(payload);
    }

    [Fact(DisplayName = "Bench-payload-004: GeneratePayload repeats pattern consistently across sizes")]
    public void Should_HaveSamePrefixBytes_When_LargerPayloadGenerated()
    {
        var small = BenchmarkBaseClass.GeneratePayload(16);
        var large = BenchmarkBaseClass.GeneratePayload(32);

        // First 16 bytes of large must equal small (deterministic pattern)
        Assert.Equal(small, large[..16]);
    }

    [Fact(DisplayName = "Bench-uri-001: CreateKestrelUri returns URI with correct host and port")]
    public void Should_ReturnCorrectUri_When_PathProvided()
    {
        var bench = new ConcreteBenchmark(5678);
        var uri = bench.CreateKestrelUri("/benchmark/simple");
        Assert.Equal("http://127.0.0.1:5678/benchmark/simple", uri.ToString());
    }

    [Fact(DisplayName = "Bench-uri-002: CreateKestrelUri uses loopback address")]
    public void Should_UseLoopbackAddress_When_CreatingUri()
    {
        var bench = new ConcreteBenchmark(9000);
        var uri = bench.CreateKestrelUri("/test");
        Assert.Equal("127.0.0.1", uri.Host);
    }

    [Fact(DisplayName = "Bench-version-001: HttpVersionValue maps '1.1' to Version11")]
    public void Should_MapVersion11String_When_HttpVersionIs11()
    {
        var bench = new ConcreteBenchmark(0) { HttpVersion = "1.1" };
        Assert.Equal(System.Net.HttpVersion.Version11, bench.HttpVersionValue);
    }

    [Fact(DisplayName = "Bench-version-002: HttpVersionValue maps '2.0' to Version20")]
    public void Should_MapVersion20String_When_HttpVersionIs20()
    {
        var bench = new ConcreteBenchmark(0) { HttpVersion = "2.0" };
        Assert.Equal(System.Net.HttpVersion.Version20, bench.HttpVersionValue);
    }

    [Fact(DisplayName = "Bench-warmup-001: WarmupRequest completes without throwing")]
    public async Task Should_CompleteWithoutThrowing_When_WarmupRequestCalled()
    {
        var bench = new ConcreteBenchmark(0);
        await bench.WarmupRequest();
        // No assertion needed — non-throwing is the contract for the base implementation
    }
}
