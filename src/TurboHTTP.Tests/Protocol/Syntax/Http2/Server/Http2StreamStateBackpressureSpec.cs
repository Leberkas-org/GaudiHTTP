using TurboHTTP.Protocol.Syntax.Http2;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Server;

public sealed class Http2StreamStateBackpressureSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.2")]
    public void MarkBodyDrainComplete_should_signal_drain_finished()
    {
        var state = new StreamState();
        state.MarkBodyDrainActive();
        Assert.True(state.HasBodyDrain);
        Assert.False(state.IsBodyDrainComplete);

        state.MarkBodyDrainComplete();
        Assert.True(state.IsBodyDrainComplete);
    }
}
