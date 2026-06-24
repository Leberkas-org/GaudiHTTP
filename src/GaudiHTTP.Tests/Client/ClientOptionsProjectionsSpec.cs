using GaudiHTTP.Client;

namespace GaudiHTTP.Tests.Client;

public sealed class ClientOptionsProjectionsSpec
{
    [Fact(Timeout = 5000)]
    public void Http2_max_frame_size_should_flow_to_encoder_options()
    {
        var o = new TurboClientOptions
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
        var enc = new TurboClientOptions().ToHttp2EncoderOptions();

        Assert.Equal(64 * 1024, enc.MaxFrameSize);
    }

    [Fact(Timeout = 5000)]
    public void Http2_header_table_size_should_flow_to_encoder_options()
    {
        var o = new TurboClientOptions
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
        var o = new TurboClientOptions
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
        var dec = new TurboClientOptions().ToHttp2DecoderOptions();

        Assert.Equal(1 * 1024 * 1024, dec.InitialStreamWindowSize);
        Assert.Equal(16 * 1024 * 1024, dec.MaxStreamWindowSize);
        Assert.Equal(1.0, dec.WindowScaleThresholdMultiplier);
        Assert.True(dec.EnableAdaptiveWindowScaling);
    }
}
