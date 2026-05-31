using TurboHTTP.Protocol.Syntax.Http2.Options;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Options;

public sealed class Http2ClientDecoderOptionsSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113")]
    public void Default_should_have_sensible_values()
    {
        Assert.Equal(100, Http2ClientDecoderOptions.Default.MaxConcurrentStreams);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113")]
    public void Validate_should_reject_invalid_MaxConcurrentStreams()
    {
        var opts = Http2ClientDecoderOptions.Default with { MaxConcurrentStreams = 0 };
        Assert.Throws<ArgumentException>(opts.Validate);
    }
}
