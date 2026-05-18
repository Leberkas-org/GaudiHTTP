using TurboHTTP.Protocol.Syntax;
using TurboHTTP.Protocol.Syntax.Http3.Options;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Options;

public sealed class Http3ServerDecoderOptionsSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void Default_should_hold_SharedHttpOptions_Default()
    {
        Assert.Same(SharedHttpOptions.Default, Http3ServerDecoderOptions.Default.Shared);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void Validate_should_delegate_to_Shared()
    {
        var bad = SharedHttpOptions.Default with { StreamingThreshold = -1 };
        var opts = Http3ServerDecoderOptions.Default with { Shared = bad };
        Assert.Throws<ArgumentException>(opts.Validate);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void Validate_should_reject_invalid_MaxConcurrentStreams()
    {
        var opts = Http3ServerDecoderOptions.Default with { MaxConcurrentStreams = 0 };
        Assert.Throws<ArgumentException>(opts.Validate);
    }
}
