using BenchmarkDotNet.Attributes;
using TurboHTTP.Context.Features;
using TurboHTTP.MicroBenchmarks.Internal;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.Syntax.Http2;
using TurboHTTP.Protocol.Syntax.Http2.Hpack;
using TurboHTTP.Protocol.Syntax.Http2.Server;

namespace TurboHTTP.MicroBenchmarks.Server;

[Config(typeof(MicroBenchmarkConfig))]
public sealed class Http2ServerDecoderBenchmark
{
    private Http2ServerDecoder _decoder = null!;
    private byte[] _simpleGetHeaders = null!;
    private byte[] _postWithHeadersAndBody = null!;
    private byte[] _manyHeaders = null!;

    [GlobalSetup]
    public void Setup()
    {
        _decoder = new Http2ServerDecoder(maxHeaderSize: 16 * 1024, maxTotalHeaderSize: 64 * 1024);

        var hpackEncoder = new HpackEncoder(useHuffman: true);

        // Simple GET request: :method, :path, :scheme, :authority
        var simpleHeaders = new List<HpackHeader>
        {
            new(WellKnownHeaders.Method, "GET"),
            new(WellKnownHeaders.Path, "/path"),
            new(WellKnownHeaders.Scheme, "https"),
            new(WellKnownHeaders.Authority, "example.com")
        };
        var output = new byte[4096];
        var span = output.AsSpan();
        var written = hpackEncoder.Encode(simpleHeaders, ref span);
        _simpleGetHeaders = output[..written];

        // Reset encoder for next benchmark
        hpackEncoder = new HpackEncoder(useHuffman: true);

        // POST with body
        var postHeaders = new List<HpackHeader>
        {
            new(WellKnownHeaders.Method, "POST"),
            new(WellKnownHeaders.Path, "/api/data"),
            new(WellKnownHeaders.Scheme, "https"),
            new(WellKnownHeaders.Authority, "example.com"),
            new("content-type", "application/json"),
            new("content-length", "1024")
        };
        output = new byte[4096];
        span = output.AsSpan();
        written = hpackEncoder.Encode(postHeaders, ref span);
        _postWithHeadersAndBody = output[..written];

        // Reset encoder for next benchmark
        hpackEncoder = new HpackEncoder(useHuffman: true);

        // Many headers
        var manyHeadersList = new List<HpackHeader>
        {
            new(WellKnownHeaders.Method, "GET"),
            new(WellKnownHeaders.Path, "/resource"),
            new(WellKnownHeaders.Scheme, "https"),
            new(WellKnownHeaders.Authority, "example.com")
        };
        for (var i = 0; i < 20; i++)
        {
            manyHeadersList.Add(new($"x-custom-header-{i}", $"value-{i}"));
        }
        output = new byte[4096];
        span = output.AsSpan();
        written = hpackEncoder.Encode(manyHeadersList, ref span);
        _manyHeaders = output[..written];
    }

    [Benchmark(Baseline = true)]
    public object? DecodeSimpleGet()
    {
        _decoder.ResetHpack();
        var state = new StreamState();
        state.AppendHeader(_simpleGetHeaders);
        return _decoder.DecodeHeadersToFeature(streamId: 1, endStream: true, state);
    }

    [Benchmark]
    public object? DecodePostWithHeaders()
    {
        _decoder.ResetHpack();
        var state = new StreamState();
        state.AppendHeader(_postWithHeadersAndBody);
        return _decoder.DecodeHeadersToFeature(streamId: 3, endStream: false, state);
    }

    [Benchmark]
    public object? DecodeManyHeaders()
    {
        _decoder.ResetHpack();
        var state = new StreamState();
        state.AppendHeader(_manyHeaders);
        return _decoder.DecodeHeadersToFeature(streamId: 5, endStream: true, state);
    }
}
