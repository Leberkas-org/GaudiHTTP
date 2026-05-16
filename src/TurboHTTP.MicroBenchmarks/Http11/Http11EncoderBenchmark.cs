using Akka.Actor;
using BenchmarkDotNet.Attributes;
using TurboHTTP.MicroBenchmarks.Internal;
using TurboHTTP.Protocol.Syntax.Http11.Client;
using TurboHTTP.Protocol.Syntax.Http11.Options;

namespace TurboHTTP.MicroBenchmarks.Http11;

[Config(typeof(MicroBenchmarkConfig))]
public class Http11EncoderBenchmark
{
    private HttpRequestMessage _simpleGet = null!;
    private HttpRequestMessage _postWithBody = null!;
    private byte[] _buffer = null!;
    private Http11ClientEncoder _encoder = null!;

    [GlobalSetup]
    public void Setup()
    {
        _buffer = new byte[16384];
        _encoder = new Http11ClientEncoder(Http11ClientEncoderOptions.Default);

        _simpleGet = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path")
        {
            Version = new Version(1, 1)
        };

        _postWithBody = new HttpRequestMessage(HttpMethod.Post, "http://example.com/api/data")
        {
            Version = new Version(1, 1)
        };
        _postWithBody.Headers.TryAddWithoutValidation("Accept", "application/json");
        _postWithBody.Headers.TryAddWithoutValidation("Authorization", "Bearer token123456789");
        _postWithBody.Headers.TryAddWithoutValidation("X-Request-Id", "perf-bench-001");
        _postWithBody.Content = new ByteArrayContent(new byte[1024]);
        _postWithBody.Content.Headers.TryAddWithoutValidation("Content-Type", "application/octet-stream");
        _postWithBody.Content.Headers.ContentLength = 1024;
    }

    [Benchmark(Baseline = true)]
    public int EncodeSimpleGet()
    {
        return _encoder.Encode(_buffer.AsSpan(), _simpleGet, ActorRefs.Nobody);
    }

    [Benchmark]
    public int EncodePostWithBody()
    {
        return _encoder.Encode(_buffer.AsSpan(), _postWithBody, ActorRefs.Nobody);
    }
}
