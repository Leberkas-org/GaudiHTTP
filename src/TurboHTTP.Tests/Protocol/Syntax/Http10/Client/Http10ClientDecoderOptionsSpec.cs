using TurboHTTP.Protocol.Syntax.Http10.Options;

namespace TurboHTTP.Tests.Protocol.Syntax.Http10.Client;

public sealed class Http10ClientDecoderOptionsSpec
{
    [Fact(Timeout = 5000)]
    public void Default_should_have_sensible_values()
    {
        Assert.Equal(64L * 1024, Http10ClientDecoderOptions.Default.StreamingThreshold);
    }
}
