using GaudiHTTP.Protocol.Multiplexed;
using GaudiHTTP.Protocol.Syntax.Http3.Client;
using GaudiHTTP.Protocol.Syntax.Http3.Qpack;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http3.Client;

/// <summary>
/// Regression tests for the H3 StreamTracker leak bug:
/// OnStreamClosed in Http3ClientSessionManager previously omitted the
/// _tracker.OnStreamClosed() call, causing the active-stream count to
/// grow monotonically and deadlock after MaxConcurrentStreams requests.
/// </summary>
public sealed class Http3StreamTrackerIntegrationSpec
{
    private static StreamManager CreateStreamManagerWithCallback(FakeClientOps ops, Action<long> onStreamClosed)
    {
        var tableSync = new QpackTableSync(0, 0, 0, 0);
        var decoder = new Http3ClientDecoder(tableSync, 32 * 1024);
        return new StreamManager(ops, decoder, tableSync, long.MaxValue)
        {
            OnStreamClosedCallback = onStreamClosed
        };
    }

    private static StreamManager CreateStreamManagerNoCallback(FakeClientOps ops)
    {
        var tableSync = new QpackTableSync(0, 0, 0, 0);
        var decoder = new Http3ClientDecoder(tableSync, 32 * 1024);
        return new StreamManager(ops, decoder, tableSync, long.MaxValue);
    }

    [Fact(Timeout = 5000)]
    public void OnStreamClosedCallback_should_release_tracker_slot_allowing_new_stream()
    {
        var ops = new FakeClientOps();
        var tracker = new QuicStreamTracker(initialNextStreamId: 0, maxConcurrentStreams: 1);
        var mgr = CreateStreamManagerWithCallback(ops, streamId => tracker.OnStreamClosed(streamId));

        var streamId = tracker.AllocateStreamId();
        tracker.OnStreamOpened(streamId);

        var state = mgr.GetOrCreateStreamState(streamId);
        state.InitResponse();

        Assert.False(tracker.CanOpenStream());

        mgr.FlushPendingResponse(streamId);

        Assert.True(tracker.CanOpenStream());
    }

    [Fact(Timeout = 5000)]
    public void OnStreamClosedCallback_should_release_all_slots_after_multiple_streams_complete()
    {
        var ops = new FakeClientOps();
        var tracker = new QuicStreamTracker(initialNextStreamId: 0, maxConcurrentStreams: 2);
        var mgr = CreateStreamManagerWithCallback(ops, streamId => tracker.OnStreamClosed(streamId));

        var id0 = tracker.AllocateStreamId();
        var id1 = tracker.AllocateStreamId();
        tracker.OnStreamOpened(id0);
        tracker.OnStreamOpened(id1);

        mgr.GetOrCreateStreamState(id0).InitResponse();
        mgr.GetOrCreateStreamState(id1).InitResponse();

        Assert.False(tracker.CanOpenStream());

        mgr.FlushPendingResponse(id0);
        Assert.True(tracker.CanOpenStream());
        Assert.Equal(1, tracker.ActiveStreamCount);

        mgr.FlushPendingResponse(id1);
        Assert.True(tracker.CanOpenStream());
        Assert.Equal(0, tracker.ActiveStreamCount);
    }

    [Fact(Timeout = 5000)]
    public void Missing_OnStreamClosedCallback_should_leave_tracker_at_capacity()
    {
        // Demonstrates the pre-fix behavior: without the callback wired,
        // the tracker slot is never released.
        var ops = new FakeClientOps();
        var tracker = new QuicStreamTracker(initialNextStreamId: 0, maxConcurrentStreams: 1);
        var mgr = CreateStreamManagerNoCallback(ops);

        var streamId = tracker.AllocateStreamId();
        tracker.OnStreamOpened(streamId);

        var state = mgr.GetOrCreateStreamState(streamId);
        state.InitResponse();

        mgr.FlushPendingResponse(streamId);

        // Without the callback, the tracker still thinks the slot is occupied
        Assert.False(tracker.CanOpenStream());
    }
}
