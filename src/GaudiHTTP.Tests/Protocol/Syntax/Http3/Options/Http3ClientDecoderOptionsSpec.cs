using GaudiHTTP.Client;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http3.Options;

public sealed class Http3ClientDecoderOptionsSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void Default_client_options_should_project_sensible_decoder_values()
    {
        Assert.Equal(100, new GaudiClientOptions().ToHttp3DecoderOptions().MaxConcurrentStreams);
    }
}
