using TurboHTTP.Protocol.Syntax.Http2.Options;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Options;

public sealed class Http2ClientEncoderOptionsSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113")]
    public void Default_should_have_sensible_values()
    {
        Assert.Equal(16 * 1024, Http2ClientEncoderOptions.Default.MaxFrameSize);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113")]
    public void Validate_should_reject_invalid_MaxFrameSize()
    {
        var opts = Http2ClientEncoderOptions.Default with { MaxFrameSize = 100 };
        Assert.Throws<ArgumentException>(opts.Validate);
    }
}
