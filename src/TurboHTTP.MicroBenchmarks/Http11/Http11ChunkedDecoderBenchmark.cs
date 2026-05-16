using Akka.Actor;
using BenchmarkDotNet.Attributes;
using TurboHTTP.MicroBenchmarks.Internal;
using TurboHTTP.Protocol.Syntax;
using TurboHTTP.Protocol.Syntax.Http11.Client;
using TurboHTTP.Protocol.Syntax.Http11.Options;

namespace TurboHTTP.MicroBenchmarks.Http11;

[Config(typeof(MicroBenchmarkConfig))]
public class Http11ChunkedDecoderBenchmark
{
    private byte[] _singleChunk = null!;
    private byte[] _manySmallChunks = null!;
    private Http11ClientDecoder _decoder = null!;

    [GlobalSetup]
    public void Setup()
    {
        _decoder = new Http11ClientDecoder(Http11ClientDecoderOptions.Default);

        _singleChunk = System.Text.Encoding.Latin1.GetBytes(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
            "100\r\n" + new string('A', 256) + "\r\n0\r\n\r\n");

        var sb = new System.Text.StringBuilder();
        sb.Append("HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n");
        for (var i = 0; i < 20; i++)
        {
            sb.Append("10\r\n");
            sb.Append(new string('B', 16));
            sb.Append("\r\n");
        }
        sb.Append("0\r\n\r\n");
        _manySmallChunks = System.Text.Encoding.Latin1.GetBytes(sb.ToString());
    }

    [Benchmark(Baseline = true)]
    public bool DecodeSingleChunk()
    {
        _decoder.Reset();
        var outcome = _decoder.Feed(_singleChunk, false, out _);
        _decoder.Reset();
        return outcome == DecodeOutcome.Complete;
    }

    [Benchmark]
    public bool Decode20SmallChunks()
    {
        _decoder.Reset();
        var outcome = _decoder.Feed(_manySmallChunks, false, out _);
        _decoder.Reset();
        return outcome == DecodeOutcome.Complete;
    }
}
