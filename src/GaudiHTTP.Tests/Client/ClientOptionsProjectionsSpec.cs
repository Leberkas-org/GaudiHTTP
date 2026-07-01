using GaudiHTTP.Client;

namespace GaudiHTTP.Tests.Client;

public sealed class ClientOptionsProjectionsSpec
{
    [Fact(Timeout = 5000)]
    public void Http2_max_frame_size_should_flow_to_encoder_options()
    {
        var o = new GaudiClientOptions
        {
            Http2 =
            {
                MaxFrameSize = 32 * 1024
            }
        };

        var enc = o.ToHttp2EncoderOptions();

        Assert.Equal(32 * 1024, enc.MaxFrameSize);
    }

    [Fact(Timeout = 5000)]
    public void Http2_default_max_frame_size_should_be_projected_not_dropped()
    {
        var enc = new GaudiClientOptions().ToHttp2EncoderOptions();

        Assert.Equal(64 * 1024, enc.MaxFrameSize);
    }

    [Fact(Timeout = 5000)]
    public void Http2_header_table_size_should_flow_to_encoder_options()
    {
        var o = new GaudiClientOptions
        {
            Http2 =
            {
                HeaderTableSize = 8 * 1024
            }
        };

        var enc = o.ToHttp2EncoderOptions();

        Assert.Equal(8 * 1024, enc.HeaderTableSize);
    }

    [Fact(Timeout = 5000)]
    public void Http2_adaptive_scaling_options_should_flow_to_decoder_options()
    {
        var o = new GaudiClientOptions
        {
            Http2 =
            {
                InitialStreamWindowSize = 128 * 1024,
                MaxStreamWindowSize = 8 * 1024 * 1024,
                WindowScaleThresholdMultiplier = 2.0,
                EnableAdaptiveWindowScaling = false,
            }
        };

        var dec = o.ToHttp2DecoderOptions();

        Assert.Equal(128 * 1024, dec.InitialStreamWindowSize);
        Assert.Equal(8 * 1024 * 1024, dec.MaxStreamWindowSize);
        Assert.Equal(2.0, dec.WindowScaleThresholdMultiplier);
        Assert.False(dec.EnableAdaptiveWindowScaling);
    }

    [Fact(Timeout = 5000)]
    public void Http2_defaults_should_start_at_1mb_with_16mb_cap()
    {
        var dec = new GaudiClientOptions().ToHttp2DecoderOptions();

        Assert.Equal(1 * 1024 * 1024, dec.InitialStreamWindowSize);
        Assert.Equal(16 * 1024 * 1024, dec.MaxStreamWindowSize);
        Assert.Equal(1.0, dec.WindowScaleThresholdMultiplier);
        Assert.True(dec.EnableAdaptiveWindowScaling);
    }

    [Fact(Timeout = 5000)]
    public void Http2_single_header_field_limit_should_track_response_header_list_size()
    {
        // Regression: MaxHeaderSize (single-field RFC 9113 §10.5.1 guard) was hardcoded to 16 KiB,
        // so raising MaxResponseHeaderListSize to accept large headers still rejected any single
        // field over 16 KiB. The single-field limit must track the configured list size.
        var o = new GaudiClientOptions
        {
            Http2 =
            {
                MaxResponseHeaderListSize = 256 * 1024
            }
        };

        var dec = o.ToHttp2DecoderOptions();

        Assert.Equal(256 * 1024, dec.MaxHeaderSize);
        Assert.Equal(256 * 1024, dec.MaxHeaderListSize);
    }

    [Fact(Timeout = 5000)]
    public void Http11_chunked_control_line_length_should_flow_to_decoder_options()
    {
        var o = new GaudiClientOptions
        {
            Http1 = { MaxChunkedControlLineLength = 32 * 1024 }
        };

        var dec = o.ToHttp11DecoderOptions();

        Assert.Equal(32 * 1024, dec.MaxChunkedControlLineLength);
    }

    [Fact(Timeout = 5000)]
    public void Http11_chunked_trailer_size_should_flow_to_decoder_options()
    {
        var o = new GaudiClientOptions
        {
            Http1 = { MaxChunkedTrailerSize = 16 * 1024 }
        };

        var dec = o.ToHttp11DecoderOptions();

        Assert.Equal(16 * 1024, dec.MaxChunkedTrailerSize);
    }

    [Fact(Timeout = 5000)]
    public void MaxBufferedBodySize_default_should_be_64_KiB()
    {
        var o = new GaudiClientOptions();

        Assert.Equal(64 * 1024, o.MaxBufferedBodySize);
    }

    [Fact(Timeout = 5000)]
    public void BodyChunkSize_default_should_be_16_KiB()
    {
        var o = new GaudiClientOptions();

        Assert.Equal(16 * 1024, o.BodyChunkSize);
    }

    [Fact(Timeout = 5000)]
    public void Tier2_overrides_should_default_to_null()
    {
        var o = new GaudiClientOptions();

        Assert.Null(o.MaxBufferedRequestBodySize);
        Assert.Null(o.MaxBufferedResponseBodySize);
        Assert.Null(o.RequestBodyChunkSize);
    }

    [Fact(Timeout = 5000)]
    public void Http1_per_protocol_body_overrides_should_default_to_null()
    {
        var o = new Http1ClientOptions();

        Assert.Null(o.MaxBufferedRequestBodySize);
        Assert.Null(o.MaxBufferedResponseBodySize);
        Assert.Null(o.RequestBodyChunkSize);
    }

    [Fact(Timeout = 5000)]
    public void Http2_per_protocol_body_overrides_should_default_to_null()
    {
        var o = new Http2ClientOptions();

        Assert.Null(o.MaxBufferedRequestBodySize);
        Assert.Null(o.MaxBufferedResponseBodySize);
        Assert.Null(o.RequestBodyChunkSize);
    }

    [Fact(Timeout = 5000)]
    public void Http3_per_protocol_body_overrides_should_default_to_null()
    {
        var o = new Http3ClientOptions();

        Assert.Null(o.MaxBufferedRequestBodySize);
        Assert.Null(o.MaxBufferedResponseBodySize);
        Assert.Null(o.RequestBodyChunkSize);
    }

    [Fact(Timeout = 5000)]
    public void Http10_response_buffer_should_resolve_from_global_default()
    {
        var o = new GaudiClientOptions { MaxBufferedBodySize = 128 * 1024 };

        var dec = o.ToHttp10DecoderOptions();

        Assert.Equal(128 * 1024, dec.MaxBufferedBodySize);
    }

    [Fact(Timeout = 5000)]
    public void Http10_response_buffer_tier2_should_override_global()
    {
        var o = new GaudiClientOptions
        {
            MaxBufferedBodySize = 128 * 1024,
            MaxBufferedResponseBodySize = 32 * 1024
        };

        var dec = o.ToHttp10DecoderOptions();

        Assert.Equal(32 * 1024, dec.MaxBufferedBodySize);
    }

    [Fact(Timeout = 5000)]
    public void Http10_response_buffer_tier3_should_override_tier2()
    {
        var o = new GaudiClientOptions
        {
            MaxBufferedBodySize = 128 * 1024,
            MaxBufferedResponseBodySize = 32 * 1024,
            Http1 = { MaxBufferedResponseBodySize = 8 * 1024 }
        };

        var dec = o.ToHttp10DecoderOptions();

        Assert.Equal(8 * 1024, dec.MaxBufferedBodySize);
    }

    [Fact(Timeout = 5000)]
    public void Http2_request_buffer_should_resolve_three_tiers()
    {
        var global = new GaudiClientOptions { MaxBufferedBodySize = 128 * 1024 };
        var tier2 = new GaudiClientOptions
        {
            MaxBufferedBodySize = 128 * 1024,
            MaxBufferedRequestBodySize = 32 * 1024
        };
        var tier3 = new GaudiClientOptions
        {
            MaxBufferedBodySize = 128 * 1024,
            MaxBufferedRequestBodySize = 32 * 1024,
            Http2 = { MaxBufferedRequestBodySize = 8 * 1024 }
        };

        Assert.Equal(128 * 1024, global.ResolveMaxBufferedRequestBodySize(global.Http2));
        Assert.Equal(32 * 1024, tier2.ResolveMaxBufferedRequestBodySize(tier2.Http2));
        Assert.Equal(8 * 1024, tier3.ResolveMaxBufferedRequestBodySize(tier3.Http2));
    }
}
