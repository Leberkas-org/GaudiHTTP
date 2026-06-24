using TurboHTTP.Client;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Options;

public sealed class Http3ClientEncoderOptionsSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void Default_client_options_should_project_sensible_encoder_values()
    {
        Assert.Equal(100, new TurboClientOptions().ToHttp3EncoderOptions().QpackBlockedStreams);
    }
}
