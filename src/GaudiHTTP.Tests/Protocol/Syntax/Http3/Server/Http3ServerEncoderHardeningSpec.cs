using System.Buffers;
using Microsoft.AspNetCore.Http.Features;
using GaudiHTTP.Protocol.Syntax.Http3;
using GaudiHTTP.Protocol.Syntax.Http3.Options;
using GaudiHTTP.Protocol.Syntax.Http3.Qpack;
using GaudiHTTP.Protocol.Syntax.Http3.Server;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http3.Server;

public sealed class Http3ServerEncoderHardeningSpec
{
    private readonly QpackTableSync _encoderTableSync = new(encoderMaxCapacity: 4096, decoderMaxCapacity: 4096, maxBlockedStreams: 100, configuredEncoderLimit: null);
    private readonly QpackTableSync _decoderTableSync = new(encoderMaxCapacity: 4096, decoderMaxCapacity: 4096, maxBlockedStreams: 100, configuredEncoderLimit: null);
    private readonly Http3ServerEncoder _encoder;

    public Http3ServerEncoderHardeningSpec()
    {
        var options = new Http3ServerEncoderOptions
        {
            WriteDateHeader = false,
            QpackMaxTableCapacity = 4096,
            QpackBlockedStreams = 100,
            MaxHeaderBytes = 8192,
            UseHuffman = true
        };
        _encoder = new Http3ServerEncoder(_encoderTableSync, options);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.2")]
    public void EncodeHeaders_status_should_be_first()
    {
        var ctx = ServerTestContext.CreateH3Response(streamId: 1, statusCode: 201);
        ctx.Get<IHttpResponseFeature>()?.Headers["x-test"] = "value";
        ctx.Get<IHttpResponseBodyFeature>()?.Writer.Write("test"u8.ToArray());

        var frame = _encoder.EncodeHeaders(ctx);

        var decoded = DecodeFrame(frame);

        Assert.NotEmpty(decoded);
        Assert.Equal(":status", decoded[0].Name);
        Assert.Equal("201", decoded[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void EncodeHeaders_should_filter_forbidden_headers()
    {
        var ctx = ServerTestContext.CreateH3Response(streamId: 1, statusCode: 200);
        ctx.Get<IHttpResponseFeature>()?.Headers["connection"] = "close";
        ctx.Get<IHttpResponseFeature>()?.Headers["transfer-encoding"] = "chunked";
        ctx.Get<IHttpResponseFeature>()?.Headers["x-allowed"] = "yes";

        var frame = _encoder.EncodeHeaders(ctx);

        var decoded = DecodeFrame(frame);

        Assert.DoesNotContain(decoded, h => h.Name == "connection");
        Assert.DoesNotContain(decoded, h => h.Name == "transfer-encoding");
        Assert.Contains(decoded, h => h is { Name: "x-allowed", Value: "yes" });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void EncodeHeaders_should_lowercase_header_names()
    {
        var ctx = ServerTestContext.CreateH3Response(streamId: 1, statusCode: 200);
        ctx.Get<IHttpResponseFeature>()?.Headers["X-Custom-Header"] = "test-value";
        ctx.Get<IHttpResponseFeature>()?.Headers["Server"] = "TestServer";

        var frame = _encoder.EncodeHeaders(ctx);

        var decoded = DecodeFrame(frame);

        Assert.Contains(decoded, h => h is { Name: "x-custom-header", Value: "test-value" });
        Assert.Contains(decoded, h => h is { Name: "server", Value: "TestServer" });
        Assert.DoesNotContain(decoded, h => h.Name == "X-Custom-Header");
        Assert.DoesNotContain(decoded, h => h.Name == "Server");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void EncodeHeaders_should_include_content_headers()
    {
        var ctx = ServerTestContext.CreateH3Response(streamId: 1, statusCode: 200);
        ctx.Get<IHttpResponseFeature>()?.Headers["content-type"] = "application/json";
        ctx.Get<IHttpResponseFeature>()?.Headers["content-length"] = "4";
        ctx.Get<IHttpResponseBodyFeature>()?.Writer.Write("data"u8.ToArray());

        var frame = _encoder.EncodeHeaders(ctx);

        var decoded = DecodeFrame(frame);

        Assert.Contains(decoded, h => h.Name == "content-type" && h.Value.Contains("application/json"));
        Assert.Contains(decoded, h => h is { Name: "content-length", Value: "4" });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3")]
    public void EncodeHeaders_multiple_responses_should_not_cross_contaminate()
    {
        var ctx1 = ServerTestContext.CreateH3Response(streamId: 1, statusCode: 200);
        ctx1.Get<IHttpResponseFeature>()?.Headers["x-first"] = "first-value";

        var ctx2 = ServerTestContext.CreateH3Response(streamId: 3, statusCode: 200);
        ctx2.Get<IHttpResponseFeature>()?.Headers["x-second"] = "second-value";

        // Encode response1 with its own encoder/decoder pair
        var encoder1Sync = new QpackTableSync(encoderMaxCapacity: 4096, decoderMaxCapacity: 4096, maxBlockedStreams: 100, configuredEncoderLimit: null);
        var options1 = new Http3ServerEncoderOptions
        {
            WriteDateHeader = false,
            QpackMaxTableCapacity = 4096,
            QpackBlockedStreams = 100,
            MaxHeaderBytes = 8192,
            UseHuffman = true
        };
        var encoder1 = new Http3ServerEncoder(encoder1Sync, options1);
        var frame1 = encoder1.EncodeHeaders(ctx1);

        var decoderSync1 = new QpackTableSync(encoderMaxCapacity: 4096, decoderMaxCapacity: 4096, maxBlockedStreams: 100, configuredEncoderLimit: null);
        if (!encoder1.EncoderInstructions.IsEmpty)
        {
            decoderSync1.ProcessEncoderInstructions(encoder1.EncoderInstructions.Span);
        }

        var decoded1 = decoderSync1.Decoder.Decode(frame1.HeaderBlock.Span, streamId: 1);

        // Encode response2 with its own encoder/decoder pair
        var encoder2Sync = new QpackTableSync(encoderMaxCapacity: 4096, decoderMaxCapacity: 4096, maxBlockedStreams: 100, configuredEncoderLimit: null);
        var options2 = new Http3ServerEncoderOptions
        {
            WriteDateHeader = false,
            QpackMaxTableCapacity = 4096,
            QpackBlockedStreams = 100,
            MaxHeaderBytes = 8192,
            UseHuffman = true
        };
        var encoder2 = new Http3ServerEncoder(encoder2Sync, options2);
        var frame2 = encoder2.EncodeHeaders(ctx2);

        var decoderSync2 = new QpackTableSync(encoderMaxCapacity: 4096, decoderMaxCapacity: 4096, maxBlockedStreams: 100, configuredEncoderLimit: null);
        if (!encoder2.EncoderInstructions.IsEmpty)
        {
            decoderSync2.ProcessEncoderInstructions(encoder2.EncoderInstructions.Span);
        }

        var decoded2 = decoderSync2.Decoder.Decode(frame2.HeaderBlock.Span, streamId: 3);

        // Verify each response has its own headers, not the other's
        var names1 = decoded1.Select(h => h.Name).ToList();
        var names2 = decoded2.Select(h => h.Name).ToList();

        Assert.Contains("x-first", names1);
        Assert.DoesNotContain("x-second", names1);

        Assert.Contains("x-second", names2);
        Assert.DoesNotContain("x-first", names2);
    }

    private IReadOnlyList<(string Name, string Value)> DecodeFrame(HeadersFrame frame)
    {
        var instructions = _encoder.EncoderInstructions;
        if (!instructions.IsEmpty)
        {
            _decoderTableSync.ProcessEncoderInstructions(instructions.Span);
        }

        return _decoderTableSync.Decoder.Decode(frame.HeaderBlock.Span, streamId: 1);
    }
}