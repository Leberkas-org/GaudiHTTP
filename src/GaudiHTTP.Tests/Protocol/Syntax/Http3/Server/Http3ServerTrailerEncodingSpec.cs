using Microsoft.AspNetCore.Http;
using GaudiHTTP.Protocol.Syntax.Http3;
using GaudiHTTP.Protocol.Syntax.Http3.Options;
using GaudiHTTP.Protocol.Syntax.Http3.Qpack;
using GaudiHTTP.Protocol.Syntax.Http3.Server;
using GaudiHTTP.Server.Context;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http3.Server;

public sealed class Http3ServerTrailerEncodingSpec
{
    private sealed class EncoderDecoderPair
    {
        public required Http3ServerEncoder Encoder { get; init; }
        public required QpackDecoder Decoder { get; init; }
        public required QpackTableSync EncoderTableSync { get; init; }
        public required QpackTableSync DecoderTableSync { get; init; }
    }

    private static EncoderDecoderPair CreateEncoderAndDecoder()
    {
        var encoderTableSync = new QpackTableSync(encoderMaxCapacity: 4096, decoderMaxCapacity: 4096, maxBlockedStreams: 100, configuredEncoderLimit: null);
        var decoderTableSync = new QpackTableSync(encoderMaxCapacity: 4096, decoderMaxCapacity: 4096, maxBlockedStreams: 100, configuredEncoderLimit: null);

        var options = new Http3ServerEncoderOptions
        {
            WriteDateHeader = false,
            QpackMaxTableCapacity = 4096,
            QpackBlockedStreams = 100,
            MaxHeaderBytes = 8192,
            UseHuffman = true
        };

        var encoder = new Http3ServerEncoder(encoderTableSync, options);
        var decoder = decoderTableSync.Decoder;

        return new EncoderDecoderPair
        {
            Encoder = encoder,
            Decoder = decoder,
            EncoderTableSync = encoderTableSync,
            DecoderTableSync = decoderTableSync
        };
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void EncodeTrailers_should_produce_HEADERS_frame()
    {
        var pair = CreateEncoderAndDecoder();
        var trailers = new GaudiHeaderDictionary
        {
            { "grpc-status", "0" },
            { "grpc-message", "OK" }
        };

        var frame = pair.Encoder.EncodeTrailers(trailers);

        Assert.NotNull(frame);
        Assert.IsType<HeadersFrame>(frame);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void EncodeTrailers_should_qpack_encode_trailer_fields()
    {
        var pair = CreateEncoderAndDecoder();
        var trailers = new GaudiHeaderDictionary
        {
            { "x-checksum", "abc123" }
        };

        var frame = pair.Encoder.EncodeTrailers(trailers)!;

        // Synchronize encoder instructions to decoder's table
        var encoderInstructions = pair.EncoderTableSync.Encoder.EncoderInstructions;
        if (!encoderInstructions.IsEmpty)
        {
            pair.DecoderTableSync.ProcessEncoderInstructions(encoderInstructions.Span);
        }

        var decoded = pair.Decoder.Decode(frame.HeaderBlock.Span);

        Assert.Contains(decoded, h => h.Name == "x-checksum" && h.Value == "abc123");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.5.1")]
    public void EncodeTrailers_should_filter_prohibited_fields()
    {
        var pair = CreateEncoderAndDecoder();
        var trailers = new GaudiHeaderDictionary
        {
            { "grpc-status", "0" },
            { "transfer-encoding", "chunked" },
            { "content-length", "42" }
        };

        var frame = pair.Encoder.EncodeTrailers(trailers)!;

        // Synchronize encoder instructions to decoder's table
        var encoderInstructions = pair.EncoderTableSync.Encoder.EncoderInstructions;
        if (!encoderInstructions.IsEmpty)
        {
            pair.DecoderTableSync.ProcessEncoderInstructions(encoderInstructions.Span);
        }

        var decoded = pair.Decoder.Decode(frame.HeaderBlock.Span);

        Assert.Contains(decoded, h => h.Name == "grpc-status" && h.Value == "0");
        Assert.DoesNotContain(decoded, h => h.Name == "transfer-encoding");
        Assert.DoesNotContain(decoded, h => h.Name == "content-length");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void EncodeTrailers_should_return_null_when_all_filtered()
    {
        var pair = CreateEncoderAndDecoder();
        var trailers = new GaudiHeaderDictionary
        {
            { "transfer-encoding", "chunked" }
        };

        var frame = pair.Encoder.EncodeTrailers(trailers);

        Assert.Null(frame);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void EncodeTrailers_should_not_include_pseudo_headers()
    {
        var pair = CreateEncoderAndDecoder();
        var trailers = new GaudiHeaderDictionary
        {
            { "x-checksum", "abc123" }
        };

        var frame = pair.Encoder.EncodeTrailers(trailers)!;

        // Synchronize encoder instructions to decoder's table
        var encoderInstructions = pair.EncoderTableSync.Encoder.EncoderInstructions;
        if (!encoderInstructions.IsEmpty)
        {
            pair.DecoderTableSync.ProcessEncoderInstructions(encoderInstructions.Span);
        }

        var decoded = pair.Decoder.Decode(frame.HeaderBlock.Span);

        Assert.DoesNotContain(decoded, h => h.Name.StartsWith(':'));
    }
}
