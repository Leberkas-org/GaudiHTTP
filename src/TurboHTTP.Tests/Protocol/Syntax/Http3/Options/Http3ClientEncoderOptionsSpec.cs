using TurboHTTP.Protocol.Syntax.Http3.Options;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Options;

public sealed class Http3ClientEncoderOptionsSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void Default_should_have_sensible_values()
    {
        Assert.Equal(100, Http3ClientEncoderOptions.Default.QpackBlockedStreams);
    }
}
