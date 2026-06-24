using Microsoft.AspNetCore.Http.Features;
using GaudiHTTP.Protocol.Syntax.Http2;
using GaudiHTTP.Protocol.Syntax.Http2.Hpack;
using GaudiHTTP.Protocol.Syntax.Http2.Options;
using GaudiHTTP.Protocol.Syntax.Http2.Server;
using GaudiHTTP.Server.Context;
using GaudiHTTP.Server.Context.Features;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Server;

public sealed class Http2ServerTrailerEncodingSpec
{
    private static Http2ServerEncoderOptions DefaultEncoderOptions() => new()
    {
        MaxFrameSize = 16 * 1024,
        HeaderTableSize = 4096,
        WriteDateHeader = false,
        MaxHeaderBytes = 32 * 1024,
        UseHuffman = true
    };

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void TrailerFeature_should_store_and_retrieve_trailer_headers()
    {
        var feature = new GaudiHttpResponseTrailersFeature
        {
            Trailers =
            {
                ["grpc-status"] = "0",
                ["grpc-message"] = "OK"
            }
        };

        Assert.Equal("0", feature.Trailers["grpc-status"]);
        Assert.Equal("OK", feature.Trailers["grpc-message"]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.5.1")]
    public void TrailerFeature_should_reject_prohibited_trailer_fields()
    {
        var feature = new GaudiHttpResponseTrailersFeature
        {
            Trailers =
            {
                ["transfer-encoding"] = "chunked"
            }
        };

        Assert.DoesNotContain(feature.GetAllowedTrailers(),
            h => h.Key.Equals("transfer-encoding", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void ResponseTrailersFeature_should_store_and_expose_trailers()
    {
        var features = new TurboFeatureCollection();
        features.Set<IHttpRequestFeature>(new GaudiHttpRequestFeature());
        features.Set<IHttpResponseFeature>(new GaudiHttpResponseFeature());
        var trailersFeature = new GaudiHttpResponseTrailersFeature();
        features.Set<IHttpResponseTrailersFeature>(trailersFeature);

        // Set trailers directly on the feature
        trailersFeature.Trailers["grpc-status"] = "0";
        trailersFeature.Trailers["grpc-message"] = "OK";

        // Verify trailers are stored
        Assert.Equal("0", trailersFeature.Trailers["grpc-status"]);
        Assert.Equal("OK", trailersFeature.Trailers["grpc-message"]);

        // Verify we can retrieve them via the feature
        var retrieved = features.Get<IHttpResponseTrailersFeature>();
        Assert.NotNull(retrieved);
        Assert.Equal("0", retrieved.Trailers["grpc-status"]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void Encoder_should_produce_trailing_HEADERS_frame_with_END_STREAM()
    {
        var encoder = new Http2ServerEncoder(DefaultEncoderOptions());
        var trailers = new TurboHeaderDictionary
        {
            { "grpc-status", "0" },
            { "grpc-message", "OK" }
        };

        var frames = encoder.EncodeTrailers(streamId: 1, trailers);

        Assert.NotEmpty(frames);
        var lastFrame = frames[^1];
        Assert.IsType<HeadersFrame>(lastFrame);
        var headersFrame = (HeadersFrame)lastFrame;
        Assert.True(headersFrame.EndStream, "Trailer HEADERS frame should have EndStream=true");
        Assert.True(headersFrame.EndHeaders, "Trailer HEADERS frame should have EndHeaders=true");
        Assert.Equal(1, headersFrame.StreamId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.5.1")]
    public void Encoder_should_filter_prohibited_trailer_fields()
    {
        var encoder = new Http2ServerEncoder(DefaultEncoderOptions());
        var decoder = new HpackDecoder();

        var trailers = new TurboHeaderDictionary
        {
            { "grpc-status", "0" },
            { "transfer-encoding", "chunked" },
            { "content-length", "42" }
        };

        var frames = encoder.EncodeTrailers(streamId: 1, trailers);

        Assert.NotEmpty(frames);

        var headerBlockBytes = new List<byte>();
        foreach (var frame in frames)
        {
            if (frame is HeadersFrame hf)
            {
                headerBlockBytes.AddRange(hf.HeaderBlockFragment.Span.ToArray());
            }
            else if (frame is ContinuationFrame cf)
            {
                headerBlockBytes.AddRange(cf.HeaderBlockFragment.Span.ToArray());
            }
        }

        var decodedHeaders = decoder.Decode(headerBlockBytes.ToArray());

        var grpcStatusHeader = decodedHeaders.FirstOrDefault(h => h.Name == "grpc-status");
        Assert.NotNull(grpcStatusHeader.Name);
        Assert.Equal("0", grpcStatusHeader.Value);

        Assert.DoesNotContain(decodedHeaders, h => h.Name == "transfer-encoding");
        Assert.DoesNotContain(decodedHeaders, h => h.Name == "content-length");
    }
}