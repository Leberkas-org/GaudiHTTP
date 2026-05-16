using BenchmarkDotNet.Attributes;
using TurboHTTP.MicroBenchmarks.Internal;
using TurboHTTP.Protocol.Syntax.Http2;
using TurboHTTP.Protocol.Syntax.Http2.Client;
using TurboHTTP.Protocol.Syntax.Http2.Hpack;

namespace TurboHTTP.MicroBenchmarks.Http2;

[Config(typeof(MicroBenchmarkConfig))]
public class Http2ResponseDecoderBenchmark
{
    private HpackEncoder _hpackEncoder = null!;
    private Http2ClientDecoder _responseDecoder = null!;
    private byte[] _encodedHeaders = null!;

    [GlobalSetup]
    public void Setup()
    {
        _hpackEncoder = new HpackEncoder(useHuffman: true);
        _responseDecoder = new Http2ClientDecoder();

        var headers = new List<HpackHeader>
        {
            new(":status", "200"),
            new("content-type", "application/json"),
            new("content-length", "1024"),
            new("server", "TurboBench"),
            new("date", "Sat, 10 May 2026 12:00:00 GMT")
        };

        var output = new byte[4096];
        var span = output.AsSpan();
        var written = _hpackEncoder.Encode(headers, ref span);
        _encodedHeaders = output[..written];
    }

    [Benchmark(Baseline = true)]
    public HttpResponseMessage? DecodeTypicalResponse()
    {
        var state = new StreamState();
        state.AppendHeader(_encodedHeaders);
        return _responseDecoder.DecodeHeaders(1, endStream: true, state);
    }
}
