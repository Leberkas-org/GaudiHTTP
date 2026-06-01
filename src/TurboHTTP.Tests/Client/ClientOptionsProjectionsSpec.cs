using TurboHTTP.Client;

namespace TurboHTTP.Tests.Client;

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
}
