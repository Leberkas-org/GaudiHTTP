using BenchmarkDotNet.Attributes;
using TurboHTTP.MicroBenchmarks.Internal;
using TurboHTTP.Protocol.Syntax;
using TurboHTTP.Protocol.Syntax.Http11.Options;
using TurboHTTP.Protocol.Syntax.Http11.Server;

namespace TurboHTTP.MicroBenchmarks.Server;

[Config(typeof(MicroBenchmarkConfig))]
public sealed class Http11ServerDecoderBenchmark
{
    private byte[] _simpleGet = null!;
    private byte[] _postWithBody = null!;
    private byte[] _manyHeaders = null!;
    private Http11ServerDecoder _decoder = null!;

    [GlobalSetup]
    public void Setup()
    {
        _decoder = new Http11ServerDecoder(Http11ServerDecoderOptions.Default);

        _simpleGet = "GET /path HTTP/1.1\r\nHost: example.com\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var postBody = new byte[1024];
        Array.Fill(postBody, (byte)'X');
        _postWithBody = System.Text.Encoding.Latin1.GetBytes(
            "POST /api/data HTTP/1.1\r\nHost: example.com\r\nContent-Length: 1024\r\nContent-Type: application/octet-stream\r\n\r\n");
        var combined = new byte[_postWithBody.Length + postBody.Length];
        Array.Copy(_postWithBody, 0, combined, 0, _postWithBody.Length);
        Array.Copy(postBody, 0, combined, _postWithBody.Length, postBody.Length);
        _postWithBody = combined;

        var headers = new System.Text.StringBuilder();
        headers.Append("GET /resource HTTP/1.1\r\n");
        headers.Append("Host: example.com\r\n");
        for (var i = 0; i < 20; i++)
        {
            headers.Append($"X-Custom-Header-{i}: value-{i}\r\n");
        }
        headers.Append("Content-Length: 0\r\n\r\n");
        _manyHeaders = System.Text.Encoding.Latin1.GetBytes(headers.ToString());
    }

    [Benchmark(Baseline = true)]
    public bool DecodeSimpleGet()
    {
        _decoder.Reset();
        var outcome = _decoder.Feed(_simpleGet, out _);
        return outcome == DecodeOutcome.Complete;
    }

    [Benchmark]
    public bool DecodePostWithBody()
    {
        _decoder.Reset();
        var outcome = _decoder.Feed(_postWithBody, out _);
        return outcome == DecodeOutcome.Complete;
    }

    [Benchmark]
    public bool DecodeManyHeaders()
    {
        _decoder.Reset();
        var outcome = _decoder.Feed(_manyHeaders, out _);
        return outcome == DecodeOutcome.Complete;
    }
}
