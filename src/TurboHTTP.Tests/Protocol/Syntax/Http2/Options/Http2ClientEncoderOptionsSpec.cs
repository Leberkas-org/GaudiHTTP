using TurboHTTP.Protocol.Syntax;
using TurboHTTP.Protocol.Syntax.Http2.Options;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Options;

public sealed class Http2ClientEncoderOptionsSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113")]
    public void Default_should_hold_SharedHttpOptions_Default()
    {
        Assert.Same(SharedHttpOptions.Default, Http2ClientEncoderOptions.Default.Shared);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113")]
    public void Validate_should_delegate_to_Shared()
    {
        var bad = SharedHttpOptions.Default with { StreamingThreshold = -1 };
        var opts = Http2ClientEncoderOptions.Default with { Shared = bad };
        Assert.Throws<ArgumentException>(opts.Validate);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113")]
    public void Validate_should_reject_invalid_MaxFrameSize()
    {
        var opts = Http2ClientEncoderOptions.Default with { MaxFrameSize = 100 };
        Assert.Throws<ArgumentException>(opts.Validate);
    }
}
