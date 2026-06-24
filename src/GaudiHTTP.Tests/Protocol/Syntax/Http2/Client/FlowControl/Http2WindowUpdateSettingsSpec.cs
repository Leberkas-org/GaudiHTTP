using TurboHTTP.Protocol.Syntax.Http2;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Client.FlowControl;

public sealed class Http2WindowUpdateSettingsSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void FlowController_should_adjust_existing_stream_windows_when_initial_window_size_increases()
    {
        var flow = new FlowController(65535, 65535);
        flow.InitStreamSendWindow(1);
        flow.InitStreamSendWindow(3);

        var settings = new SettingsFrame([(SettingsParameter.InitialWindowSize, 131070u)]);
        flow.OnRemoteSettings(settings);

        Assert.Equal(65535, flow.GetSendWindow(1));
        Assert.Equal(65535, flow.GetSendWindow(3));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void FlowController_should_reduce_existing_stream_windows_when_initial_window_size_decreases()
    {
        var flow = new FlowController(65535, 65535);
        flow.InitStreamSendWindow(1);

        flow.OnDataSent(1, 30000);

        var settings = new SettingsFrame([(SettingsParameter.InitialWindowSize, 32768u)]);
        flow.OnRemoteSettings(settings);

        Assert.Equal(32768 - 30000, flow.GetSendWindow(1));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void FlowController_should_not_affect_new_streams_when_window_is_negative_from_settings_change()
    {
        var flow = new FlowController(65535, 65535);
        flow.InitStreamSendWindow(1);
        flow.OnDataSent(1, 60000);

        var settings = new SettingsFrame([(SettingsParameter.InitialWindowSize, 1024u)]);
        flow.OnRemoteSettings(settings);

        flow.InitStreamSendWindow(3);
        Assert.Equal(1024, flow.GetSendWindow(3));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void StreamTracker_should_allow_streams_when_max_concurrent_is_max_value()
    {
        var tracker = new StreamTracker(initialNextStreamId: 1, maxConcurrentStreams: int.MaxValue);

        Assert.True(tracker.CanOpenStream());
        var id = tracker.AllocateStreamId();
        tracker.OnStreamOpened(id);
        Assert.True(tracker.CanOpenStream());
    }
}