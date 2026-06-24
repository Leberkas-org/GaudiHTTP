using GaudiHTTP.Protocol.Syntax.Http2;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Client;

public sealed class Http2ClientBodyBackpressureSpec
{
    [Fact(Timeout = 5000)]
    public void StreamState_should_track_body_drain_lifecycle()
    {
        var state = new StreamState();

        Assert.False(state.HasBodyDrain);
        Assert.False(state.IsBodyDrainComplete);

        state.MarkBodyDrainActive();
        Assert.True(state.HasBodyDrain);
        Assert.False(state.IsBodyDrainComplete);

        state.MarkBodyDrainComplete();
        Assert.True(state.HasBodyDrain);
        Assert.True(state.IsBodyDrainComplete);
    }

    [Fact(Timeout = 5000)]
    public void StreamState_should_reset_body_drain_state()
    {
        var state = new StreamState();
        state.MarkBodyDrainActive();
        state.MarkBodyDrainComplete();

        state.Reset();

        Assert.False(state.HasBodyDrain);
        Assert.False(state.IsBodyDrainComplete);
    }
}
