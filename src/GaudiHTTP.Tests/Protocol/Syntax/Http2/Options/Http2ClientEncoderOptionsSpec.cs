using GaudiHTTP.Client;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Options;

public sealed class Http2ClientEncoderOptionsSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113")]
    public void Default_client_options_should_project_sensible_encoder_values()
    {
        Assert.Equal(64 * 1024, new TurboClientOptions().ToHttp2EncoderOptions().MaxFrameSize);
    }
}
