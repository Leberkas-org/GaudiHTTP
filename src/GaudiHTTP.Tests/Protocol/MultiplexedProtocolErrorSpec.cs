using TurboHTTP.Protocol;

namespace TurboHTTP.Tests.Protocol;

public sealed class MultiplexedProtocolErrorSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.4.1")]
    public void ConnectionProtocolException_should_carry_error_code()
    {
        var ex = new ConnectionProtocolException(0x9, "compression error");

        Assert.Equal(0x9, ex.ErrorCode);
        Assert.Equal("compression error", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.4.1")]
    public void ConnectionProtocolException_should_be_an_HttpProtocolException()
    {
        // Derives from HttpProtocolException so existing catch/assert sites keep working while new
        // code can catch the connection-scoped subtype to drive GOAWAY + teardown.
        var ex = new ConnectionProtocolException(0x1, "protocol error");

        Assert.IsAssignableFrom<HttpProtocolException>(ex);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.4.2")]
    public void StreamProtocolException_should_carry_stream_id_and_error_code()
    {
        var ex = new StreamProtocolException(streamId: 3, errorCode: 0x1, "stream error");

        Assert.Equal(3, ex.StreamId);
        Assert.Equal(0x1, ex.ErrorCode);
        Assert.IsAssignableFrom<HttpProtocolException>(ex);
    }
}
