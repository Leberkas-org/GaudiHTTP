using TurboHTTP.Server;

namespace TurboHTTP.Tests.Server.Options;

public sealed class ServerOptionsProjectionsSpec
{
    [Fact(Timeout = 5000)]
    public void Override_should_win_over_limits()
    {
        var o = new TurboServerOptions
        {
            Http2 =
            {
                MaxRequestBodySize = 999,
                KeepAliveTimeout = TimeSpan.FromSeconds(7)
            }
        };

        var eff = o.ToHttp2Options();

        Assert.Equal(999, eff.Limits.MaxRequestBodySize);
        Assert.Equal(TimeSpan.FromSeconds(7), eff.Limits.KeepAliveTimeout);
    }

    [Fact(Timeout = 5000)]
    public void Null_override_should_inherit_limits()
    {
        var o = new TurboServerOptions();

        var eff = o.ToHttp2Options();

        Assert.Equal(o.Limits.MaxRequestBodySize, eff.Limits.MaxRequestBodySize);
        Assert.Equal(o.Limits.KeepAliveTimeout, eff.Limits.KeepAliveTimeout);
        Assert.Equal(o.Limits.MinResponseDataRate, eff.Limits.MinResponseDataRate);
    }

    [Fact(Timeout = 5000)]
    public void Http3_body_override_should_now_be_honored()
    {
        var o = new TurboServerOptions
        {
            Http3 =
            {
                MaxRequestBodySize = 555
            }
        };

        Assert.Equal(555, o.ToHttp3Options().Limits.MaxRequestBodySize);
    }

    [Fact(Timeout = 5000)]
    public void ToRateMonitor_should_project_four_rate_fields()
    {
        var eff = new TurboServerOptions().ToHttp2Options();

        var rate = eff.ToRateMonitor();

        Assert.Equal(eff.Limits.MinRequestBodyDataRate, rate.MinRequestBodyDataRate);
        Assert.Equal(eff.Limits.MinResponseDataRate, rate.MinResponseDataRate);
    }

    [Fact(Timeout = 5000)]
    public void Http1_chunk_extension_limit_should_flow_to_decoder_options()
    {
        var o = new TurboServerOptions
        {
            Http1 =
            {
                MaxChunkExtensionLength = 7
            }
        };

        var dec = o.ToHttp1Options().ToHttp11DecoderOptions();

        Assert.Equal(7, dec.MaxChunkExtensionLength);
    }

    [Fact(Timeout = 5000)]
    public void Header_size_should_fall_back_to_global_total_when_protocol_unset()
    {
        var o = new TurboServerOptions
        {
            Limits =
            {
                MaxRequestHeadersTotalSize = 7777
            }
        };

        Assert.Equal(7777, o.ToHttp1Options().MaxHeaderListSize);
        Assert.Equal(7777, o.ToHttp2Options().MaxHeaderListSize);
        Assert.Equal(7777, o.ToHttp3Options().MaxHeaderListSize);
    }

    [Fact(Timeout = 5000)]
    public void Header_size_protocol_override_should_win_over_global_total()
    {
        var o = new TurboServerOptions
        {
            Limits =
            {
                MaxRequestHeadersTotalSize = 7777
            },
            Http2 =
            {
                MaxHeaderListSize = 999
            }
        };

        Assert.Equal(999, o.ToHttp2Options().MaxHeaderListSize);
    }

    [Fact(Timeout = 5000)]
    public void Http2_response_buffer_limit_should_flow_to_connection_options()
    {
        var o = new TurboServerOptions
        {
            Http2 =
            {
                MaxResponseBufferSize = 4321
            }
        };

        Assert.Equal(4321, o.ToHttp2Options().MaxResponseBufferSize);
    }

    [Fact(Timeout = 5000)]
    public void MaxRequestBodySize_default_should_match_kestrel()
    {
        var o = new TurboServerOptions();

        Assert.Equal(30_000_000, o.Limits.MaxRequestBodySize);
    }

    [Fact(Timeout = 5000)]
    public void MaxResponseBufferSize_global_default_should_be_64_KiB()
    {
        var o = new TurboServerOptions();

        Assert.Equal(64 * 1024, o.Limits.MaxResponseBufferSize);
    }

    [Fact(Timeout = 5000)]
    public void Http2_MaxResponseBufferSize_should_fall_back_to_global()
    {
        var o = new TurboServerOptions
        {
            Limits = { MaxResponseBufferSize = 99_999 }
        };

        Assert.Equal(99_999, o.ToHttp2Options().MaxResponseBufferSize);
    }

    [Fact(Timeout = 5000)]
    public void Http2_MaxResponseBufferSize_override_should_win()
    {
        var o = new TurboServerOptions
        {
            Limits = { MaxResponseBufferSize = 99_999 },
            Http2 = { MaxResponseBufferSize = 42 }
        };

        Assert.Equal(42, o.ToHttp2Options().MaxResponseBufferSize);
    }

    [Fact(Timeout = 5000)]
    public void Http3_MaxResponseBufferSize_should_fall_back_to_global()
    {
        var o = new TurboServerOptions
        {
            Limits = { MaxResponseBufferSize = 88_888 }
        };

        Assert.Equal(88_888, o.ToHttp3Options().MaxResponseBufferSize);
    }

    [Fact(Timeout = 5000)]
    public void Http3_MaxResponseBufferSize_override_should_win()
    {
        var o = new TurboServerOptions
        {
            Limits = { MaxResponseBufferSize = 88_888 },
            Http3 = { MaxResponseBufferSize = 77 }
        };

        Assert.Equal(77, o.ToHttp3Options().MaxResponseBufferSize);
    }

    [Fact(Timeout = 5000)]
    public void Http3_response_buffer_limit_should_flow_to_connection_options()
    {
        var o = new TurboServerOptions
        {
            Http3 = { MaxResponseBufferSize = 5678 }
        };

        Assert.Equal(5678, o.ToHttp3Options().MaxResponseBufferSize);
    }

    [Fact(Timeout = 5000)]
    public void MaxRequestBufferSize_default_should_be_1_MiB()
    {
        var o = new TurboServerOptions();

        Assert.Equal(1024 * 1024, o.Limits.MaxRequestBufferSize);
    }

    [Fact(Timeout = 5000)]
    public void MaxOutboundCoalesceCount_default_should_be_8()
    {
        var o = new TurboServerOptions();

        Assert.Equal(8, o.MaxOutboundCoalesceCount);
    }

    [Fact(Timeout = 5000)]
    public void AllowResponseHeaderCompression_default_should_be_true()
    {
        var o = new TurboServerOptions();

        Assert.True(o.AllowResponseHeaderCompression);
    }

    [Fact(Timeout = 5000)]
    public void AllowResponseHeaderCompression_should_flow_to_h2_encoder_options()
    {
        var o = new TurboServerOptions { AllowResponseHeaderCompression = false };

        var enc = o.ToHttp2Options().ToEncoderOptions();

        Assert.False(enc.UseHuffman);
    }

    [Fact(Timeout = 5000)]
    public void AllowResponseHeaderCompression_should_flow_to_h3_encoder_options()
    {
        var o = new TurboServerOptions { AllowResponseHeaderCompression = false };

        var enc = o.ToHttp3Options().ToEncoderOptions();

        Assert.False(enc.UseHuffman);
    }

    [Fact(Timeout = 5000)]
    public void Http2_KeepAlivePingDelay_default_should_be_infinite()
    {
        var o = new TurboServerOptions();

        Assert.Equal(Timeout.InfiniteTimeSpan, o.ToHttp2Options().KeepAlivePingDelay);
    }

    [Fact(Timeout = 5000)]
    public void Http2_KeepAlivePingTimeout_default_should_be_20s()
    {
        var o = new TurboServerOptions();

        Assert.Equal(TimeSpan.FromSeconds(20), o.ToHttp2Options().KeepAlivePingTimeout);
    }

    [Fact(Timeout = 5000)]
    public void Http2_KeepAlivePing_custom_should_flow()
    {
        var o = new TurboServerOptions
        {
            Http2 =
            {
                KeepAlivePingDelay = TimeSpan.FromSeconds(15),
                KeepAlivePingTimeout = TimeSpan.FromSeconds(5)
            }
        };

        var h2 = o.ToHttp2Options();

        Assert.Equal(TimeSpan.FromSeconds(15), h2.KeepAlivePingDelay);
        Assert.Equal(TimeSpan.FromSeconds(5), h2.KeepAlivePingTimeout);
    }
}
