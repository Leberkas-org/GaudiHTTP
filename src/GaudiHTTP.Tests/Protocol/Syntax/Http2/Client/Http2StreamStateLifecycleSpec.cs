using GaudiHTTP.Protocol.Syntax.Http2;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Client;

public sealed class Http2StreamStateLifecycleSpec
{
    [Fact(Timeout = 5000)]
    public void Fresh_state_should_not_be_releasable()
    {
        var state = new StreamState();

        // No drain has been activated yet, so the local (outbound) direction is trivially
        // "done" by default (see StreamState.IsLocalSendComplete) - a fresh stream reads as
        // HalfClosedLocal, not Open, until MarkBodyDrainActive() is called.
        Assert.Equal(StreamState.StreamLifecycle.HalfClosedLocal, state.Lifecycle);
        Assert.False(state.MayRelease);
    }

    [Fact(Timeout = 5000)]
    public void Fresh_state_with_active_drain_should_be_open()
    {
        var state = new StreamState();
        state.MarkBodyDrainActive();

        Assert.Equal(StreamState.StreamLifecycle.Open, state.Lifecycle);
        Assert.False(state.MayRelease);
    }

    [Fact(Timeout = 5000)]
    public void Remote_done_without_active_drain_should_be_immediately_releasable()
    {
        var state = new StreamState();

        var releasable = state.OnRemoteDone();

        Assert.True(releasable);
        Assert.True(state.MayRelease);
        Assert.Equal(StreamState.StreamLifecycle.Closed, state.Lifecycle);
    }

    [Fact(Timeout = 5000)]
    public void Local_done_without_active_drain_should_be_immediately_releasable_once_remote_is_also_done()
    {
        var state = new StreamState();

        // No drain was ever activated - OnLocalDone reflects a synchronously-completed send that
        // never needed the pump (e.g. the whole body fit inline).
        var releasableBeforeRemote = state.OnLocalDone();
        Assert.False(releasableBeforeRemote);
        Assert.Equal(StreamState.StreamLifecycle.HalfClosedLocal, state.Lifecycle);

        var releasableAfterRemote = state.OnRemoteDone();
        Assert.True(releasableAfterRemote);
        Assert.Equal(StreamState.StreamLifecycle.Closed, state.Lifecycle);
    }

    [Fact(Timeout = 5000)]
    public void Active_drain_should_block_release_until_drain_completes_even_after_remote_closes()
    {
        var state = new StreamState();
        state.MarkBodyDrainActive();

        // Remote closes (response fully received) while the request body is still draining -
        // must NOT be releasable yet: HalfClosedRemote, not Closed.
        var releasableOnRemoteDone = state.OnRemoteDone();
        Assert.False(releasableOnRemoteDone);
        Assert.False(state.MayRelease);
        Assert.Equal(StreamState.StreamLifecycle.HalfClosedRemote, state.Lifecycle);

        // Drain completes afterward - now both directions are settled.
        var releasableOnLocalDone = state.OnLocalDone();
        Assert.True(releasableOnLocalDone);
        Assert.True(state.MayRelease);
        Assert.Equal(StreamState.StreamLifecycle.Closed, state.Lifecycle);
    }

    [Fact(Timeout = 5000)]
    public void Active_drain_completing_before_remote_closes_should_block_release_until_remote_closes()
    {
        var state = new StreamState();
        state.MarkBodyDrainActive();

        var releasableOnLocalDone = state.OnLocalDone();
        Assert.False(releasableOnLocalDone);
        Assert.Equal(StreamState.StreamLifecycle.HalfClosedLocal, state.Lifecycle);

        var releasableOnRemoteDone = state.OnRemoteDone();
        Assert.True(releasableOnRemoteDone);
        Assert.Equal(StreamState.StreamLifecycle.Closed, state.Lifecycle);
    }

    [Fact(Timeout = 5000)]
    public void Reset_should_return_lifecycle_to_open()
    {
        var state = new StreamState();
        state.MarkBodyDrainActive();
        state.OnRemoteDone();
        state.OnLocalDone();
        Assert.Equal(StreamState.StreamLifecycle.Closed, state.Lifecycle);

        state.Reset();

        Assert.Equal(StreamState.StreamLifecycle.HalfClosedLocal, state.Lifecycle);
        Assert.False(state.MayRelease);
    }
}
