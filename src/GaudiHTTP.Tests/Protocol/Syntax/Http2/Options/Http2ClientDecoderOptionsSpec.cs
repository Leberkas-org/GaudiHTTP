using GaudiHTTP.Client;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Options;

public sealed class Http2ClientDecoderOptionsSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113")]
    public void Default_client_options_should_project_sensible_decoder_values()
    {
        Assert.Equal(100, new TurboClientOptions().ToHttp2DecoderOptions().MaxConcurrentStreams);
    }
}
