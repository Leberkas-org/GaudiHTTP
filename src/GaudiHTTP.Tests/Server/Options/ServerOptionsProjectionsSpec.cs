using GaudiHTTP.Server;

namespace GaudiHTTP.Tests.Server.Options;

// CS0618: this spec intentionally exercises the obsolete-but-retained per-protocol
// MaxResponseBufferSize override to lock in its projection/fallback behaviour.
#pragma warning disable CS0618
public sealed class ServerOptionsProjectionsSpec
{
    [Fact(Timeout = 5000)]
    public void Override_should_win_over_limits()
    {
        var o = new GaudiServerOptions
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
        var o = new GaudiServerOptions();

        var eff = o.ToHttp2Options();

        Assert.Equal(o.Limits.MaxRequestBodySize, eff.Limits.MaxRequestBodySize);
        Assert.Equal(o.Limits.KeepAliveTimeout, eff.Limits.KeepAliveTimeout);
        Assert.Equal(o.Limits.MinResponseDataRate, eff.Limits.MinResponseDataRate);
    }

    [Fact(Timeout = 5000)]
    public void Http3_body_override_should_now_be_honored()
    {
        var o = new GaudiServerOptions
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
        var eff = new GaudiServerOptions().ToHttp2Options();

        var rate = eff.ToRateMonitor();

        Assert.Equal(eff.Limits.MinRequestBodyDataRate, rate.MinRequestBodyDataRate);
        Assert.Equal(eff.Limits.MinResponseDataRate, rate.MinResponseDataRate);
    }

    [Fact(Timeout = 5000)]
    public void Http1_chunk_extension_limit_should_flow_to_decoder_options()
    {
        var o = new GaudiServerOptions
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
        var o = new GaudiServerOptions
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
        var o = new GaudiServerOptions
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
    public void MaxRequestBodySize_default_should_match_kestrel()
    {
        var o = new GaudiServerOptions();

        Assert.Equal(30_000_000, o.Limits.MaxRequestBodySize);
    }

    [Fact(Timeout = 5000)]
    public void MaxResponseBufferSize_global_default_should_be_64_KiB()
    {
        var o = new GaudiServerOptions();

        Assert.Equal(64 * 1024, o.Limits.MaxResponseBufferSize);
    }

    [Fact(Timeout = 5000)]
    public void MaxRequestBufferSize_default_should_be_1_MiB()
    {
        var o = new GaudiServerOptions();

        Assert.Equal(1024 * 1024, o.Limits.MaxRequestBufferSize);
    }

    [Fact(Timeout = 5000)]
    public void AllowResponseHeaderCompression_default_should_be_true()
    {
        var o = new GaudiServerOptions();

        Assert.True(o.AllowResponseHeaderCompression);
    }

    [Fact(Timeout = 5000)]
    public void AllowResponseHeaderCompression_should_flow_to_h2_encoder_options()
    {
        var o = new GaudiServerOptions { AllowResponseHeaderCompression = false };

        var enc = o.ToHttp2Options().ToEncoderOptions();

        Assert.False(enc.UseHuffman);
    }

    [Fact(Timeout = 5000)]
    public void AllowResponseHeaderCompression_should_flow_to_h3_encoder_options()
    {
        var o = new GaudiServerOptions { AllowResponseHeaderCompression = false };

        var enc = o.ToHttp3Options().ToEncoderOptions();

        Assert.False(enc.UseHuffman);
    }

    [Fact(Timeout = 5000)]
    public void Http2_KeepAlivePingDelay_default_should_be_infinite()
    {
        var o = new GaudiServerOptions();

        Assert.Equal(Timeout.InfiniteTimeSpan, o.ToHttp2Options().KeepAlivePingDelay);
    }

    [Fact(Timeout = 5000)]
    public void Http2_KeepAlivePingTimeout_default_should_be_20s()
    {
        var o = new GaudiServerOptions();

        Assert.Equal(TimeSpan.FromSeconds(20), o.ToHttp2Options().KeepAlivePingTimeout);
    }

    [Fact(Timeout = 5000)]
    public void Http2_KeepAlivePing_custom_should_flow()
    {
        var o = new GaudiServerOptions
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

    [Fact(Timeout = 5000)]
    public void RapidResetDetectionWindow_should_flow_to_resolved_limits()
    {
        var o = new GaudiServerOptions();
        o.Limits.RapidResetDetectionWindow = TimeSpan.FromSeconds(15);

        var h2 = o.ToHttp2Options();
        var h3 = o.ToHttp3Options();

        Assert.Equal(TimeSpan.FromSeconds(15), h2.Limits.RapidResetDetectionWindow);
        Assert.Equal(TimeSpan.FromSeconds(15), h3.Limits.RapidResetDetectionWindow);
    }

    [Fact(Timeout = 5000)]
    public void RapidResetDetectionWindow_default_should_be_30_seconds()
    {
        var o = new GaudiServerOptions();

        Assert.Equal(TimeSpan.FromSeconds(30), o.ToHttp2Options().Limits.RapidResetDetectionWindow);
    }
}