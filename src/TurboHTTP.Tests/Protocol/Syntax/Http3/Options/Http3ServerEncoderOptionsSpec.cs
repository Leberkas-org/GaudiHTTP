using TurboHTTP.Protocol.Syntax;
using TurboHTTP.Protocol.Syntax.Http3.Options;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Options;

public sealed class Http3ServerEncoderOptionsSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void Default_should_hold_SharedHttpOptions_Default()
    {
        Assert.Same(SharedHttpOptions.Default, Http3ServerEncoderOptions.Default.Shared);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void Validate_should_delegate_to_Shared()
    {
        var bad = SharedHttpOptions.Default with { MaxHeaderCount = 0 };
        var opts = Http3ServerEncoderOptions.Default with { Shared = bad };
        Assert.Throws<ArgumentException>(opts.Validate);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void Validate_should_reject_invalid_QpackMaxTableCapacity()
    {
        var opts = Http3ServerEncoderOptions.Default with { QpackMaxTableCapacity = -1 };
        Assert.Throws<ArgumentException>(opts.Validate);
    }
}
