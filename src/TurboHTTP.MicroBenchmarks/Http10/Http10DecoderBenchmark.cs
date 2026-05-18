using BenchmarkDotNet.Attributes;
using TurboHTTP.MicroBenchmarks.Internal;
using TurboHTTP.Protocol.Syntax.Http10.Client;
using TurboHTTP.Protocol.Syntax.Http10.Options;

namespace TurboHTTP.MicroBenchmarks.Http10;

[Config(typeof(MicroBenchmarkConfig))]
public class Http10DecoderBenchmark
{
    private byte[] _smallResponse = null!;
    private byte[] _largeResponse = null!;
    private Http10ClientDecoder _decoder = null!;

    [GlobalSetup]
    public void Setup()
    {
        _decoder = new Http10ClientDecoder(Http10ClientDecoderOptions.Default);

        _smallResponse = "HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nHello"u8.ToArray();

        _largeResponse = System.Text.Encoding.Latin1.GetBytes(
            string.Concat("HTTP/1.0 200 OK\r\nContent-Length: 8192\r\n\r\n",
                new string('X', 8192)));
    }

    [Benchmark(Baseline = true)]
    public object DecodeSmallResponse()
    {
        _decoder.Reset();
        return _decoder.Feed(_smallResponse, requestMethodWasHead: false, out _);
    }

    [Benchmark]
    public object DecodeLargeResponse()
    {
        _decoder.Reset();
        return _decoder.Feed(_largeResponse, requestMethodWasHead: false, out _);
    }
}
