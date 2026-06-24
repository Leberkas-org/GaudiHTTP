using GaudiHTTP.Client;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http10.Client;

public sealed class Http10ClientDecoderOptionsSpec
{
    [Fact(Timeout = 5000)]
    public void Default_client_options_should_project_sensible_decoder_values()
    {
        Assert.Equal(64L * 1024, new GaudiClientOptions().ToHttp10DecoderOptions().StreamingThreshold);
    }
}
