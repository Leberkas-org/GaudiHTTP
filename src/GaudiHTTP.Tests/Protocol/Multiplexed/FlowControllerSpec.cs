using GaudiHTTP.Protocol.Syntax.Http2;

namespace GaudiHTTP.Tests.Protocol.Multiplexed;

public sealed class FlowControllerSpec
{
    [Fact(Timeout = 5000)]
    public void FlowController_should_detect_connection_flow_control_violation()
    {
        var fc = new FlowController(
            connectionWindowSize: 100,
            streamWindowSize: 65535);

        var result = fc.OnInboundData(1, 200);
        Assert.False(result.Success);
        Assert.True(result.IsConnectionViolation);
    }

    [Fact(Timeout = 5000)]
    public void FlowController_should_detect_stream_flow_control_violation()
    {
        var fc = new FlowController(
            connectionWindowSize: 65535,
            streamWindowSize: 100);

        var result = fc.OnInboundData(1, 200);
        Assert.False(result.Success);
        Assert.True(result.IsStreamViolation);
        Assert.Equal(1, result.ViolationStreamId);
    }

    [Fact(Timeout = 5000)]
    public void ApplyInitialWindowSizeDelta_should_update_all_stream_windows()
    {
        var fc = new FlowController(connectionWindowSize: 65535, streamWindowSize: 65535);
        fc.InitStreamSendWindow(1);
        fc.InitStreamSendWindow(3);

        fc.ApplyInitialWindowSizeDelta(100);

        Assert.Equal(65535 + 100, fc.GetStreamSendWindow(1));
        Assert.Equal(65535 + 100, fc.GetStreamSendWindow(3));
    }

    [Fact(Timeout = 5000)]
    public void ApplyInitialWindowSizeDelta_should_not_allocate()
    {
        var fc = new FlowController(connectionWindowSize: 65535, streamWindowSize: 65535);
        fc.InitStreamSendWindow(1);
        fc.InitStreamSendWindow(3);
        fc.ApplyInitialWindowSizeDelta(1);

        var before = GC.GetAllocatedBytesForCurrentThread();
        fc.ApplyInitialWindowSizeDelta(1);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact(Timeout = 5000)]
    public void FlowController_should_batch_window_updates()
    {
        const int windowSize = 65535;
        var fc = new FlowController(
            connectionWindowSize: windowSize,
            streamWindowSize: windowSize);

        var result = fc.OnInboundData(1, 100);
        Assert.True(result.Success);
        Assert.Null(result.ConnectionWindowUpdate);

        var threshold = Math.Max(8192, windowSize / 2);
        result = fc.OnInboundData(1, threshold);
        Assert.True(result.Success);
        Assert.NotNull(result.ConnectionWindowUpdate);
        Assert.NotNull(result.StreamWindowUpdate);
    }

    [Fact(Timeout = 5000)]
    public void FlowController_should_track_goaway()
    {
        var fc = new FlowController(connectionWindowSize: 65535, streamWindowSize: 65535);
        Assert.False(fc.GoAwayReceived);
        fc.OnGoAway();
        Assert.True(fc.GoAwayReceived);
    }

    [Fact(Timeout = 5000)]
    public void FlowController_should_return_pending_update_on_stream_close()
    {
        const int windowSize = 65535;
        var fc = new FlowController(
            connectionWindowSize: windowSize,
            streamWindowSize: windowSize);

        fc.OnInboundData(1, 100);
        var signal = fc.OnStreamClosed(1);
        Assert.NotNull(signal);
        Assert.Equal(1, signal.Value.StreamId);
        Assert.Equal(100, signal.Value.Increment);
    }

    [Fact(Timeout = 5000)]
    public void FlowController_should_reset_all_state()
    {
        var fc = new FlowController(connectionWindowSize: 65535, streamWindowSize: 65535);
        fc.OnInboundData(1, 100);
        fc.OnGoAway();
        fc.Reset(65535, 65535);
        Assert.False(fc.GoAwayReceived);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9.1")]
    public void OnSendWindowUpdate_should_throw_when_connection_window_exceeds_max()
    {
        var fc = new FlowController(connectionWindowSize: 65535, streamWindowSize: 65535);
        fc.OnSendWindowUpdate(0, int.MaxValue - 65535);

        Assert.Throws<HttpProtocolException>(() =>
            fc.OnSendWindowUpdate(0, 1));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9.1")]
    public void OnSendWindowUpdate_should_throw_when_stream_window_exceeds_max()
    {
        var fc = new FlowController(connectionWindowSize: 65535, streamWindowSize: 65535);
        fc.OnSendWindowUpdate(1, int.MaxValue - 65535);

        Assert.Throws<HttpProtocolException>(() =>
            fc.OnSendWindowUpdate(1, 1));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9.1")]
    public void OnSendWindowUpdate_should_allow_window_up_to_max()
    {
        var fc = new FlowController(connectionWindowSize: 65535, streamWindowSize: 65535);

        var ex = Record.Exception(() =>
            fc.OnSendWindowUpdate(0, int.MaxValue - 65535));

        Assert.Null(ex);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void OnDataSent_should_decrement_stream_send_window()
    {
        var fc = new FlowController(connectionWindowSize: 65535, streamWindowSize: 65535);

        fc.OnDataSent(1, 10000);

        var window = fc.GetSendWindow(1);
        Assert.Equal(65535 - 10000, window);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void OnDataSent_should_decrement_connection_window_across_all_streams()
    {
        var fc = new FlowController(connectionWindowSize: 65535, streamWindowSize: 65535);

        fc.OnDataSent(1, 30000);
        fc.OnDataSent(3, 20000);

        var freshStreamWindow = fc.GetSendWindow(99);
        Assert.Equal(65535 - 50000, freshStreamWindow);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void GetSendWindow_should_return_min_of_connection_and_stream_window()
    {
        var fc = new FlowController(connectionWindowSize: 65535, streamWindowSize: 65535);

        fc.OnDataSent(1, 60000);

        var window = fc.GetSendWindow(1);
        Assert.Equal(65535 - 60000, window);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void OnDataSent_followed_by_window_update_should_restore_send_capacity()
    {
        var fc = new FlowController(connectionWindowSize: 65535, streamWindowSize: 65535);

        fc.OnDataSent(1, 65535);
        Assert.Equal(0, fc.GetSendWindow(1));

        fc.OnSendWindowUpdate(0, 32768);
        fc.OnSendWindowUpdate(1, 32768);

        Assert.Equal(32768, fc.GetSendWindow(1));
    }

    [Fact(Timeout = 5000)]
    public void ConnectionSendWindow_should_return_initial_connection_window()
    {
        var fc = new FlowController(1024 * 1024, 64 * 1024);
        Assert.Equal(65535, fc.ConnectionSendWindow);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionSendWindow_should_decrease_after_data_sent()
    {
        var fc = new FlowController(1024 * 1024, 64 * 1024);
        fc.InitStreamSendWindow(1);
        fc.OnDataSent(1, 1000);
        Assert.Equal(65535 - 1000, fc.ConnectionSendWindow);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionSendWindow_should_increase_after_window_update()
    {
        var fc = new FlowController(1024 * 1024, 64 * 1024);
        fc.OnSendWindowUpdate(0, 10000);
        Assert.Equal(65535 + 10000, fc.ConnectionSendWindow);
    }

    [Fact(Timeout = 5000)]
    public void GetStreamSendWindow_should_return_initial_when_no_data_sent()
    {
        var fc = new FlowController(1024 * 1024, 64 * 1024);
        fc.InitStreamSendWindow(1);
        Assert.Equal(65535, fc.GetStreamSendWindow(1));
    }

    [Fact(Timeout = 5000)]
    public void GetStreamSendWindow_should_return_only_stream_window_not_connection_min()
    {
        var fc = new FlowController(1024 * 1024, 64 * 1024);
        fc.InitStreamSendWindow(1);
        fc.OnDataSent(1, 60000);
        // Connection window: 65535 - 60000 = 5535
        // Stream window: 65535 - 60000 = 5535
        // GetSendWindow(1) returns min(5535, 5535) = 5535
        // GetStreamSendWindow(1) returns 5535 (just stream)
        Assert.Equal(65535 - 60000, fc.GetStreamSendWindow(1));

        // Now increase connection window only
        fc.OnSendWindowUpdate(0, 60000);
        // Connection: 65535, Stream: 5535
        // GetSendWindow(1) returns 5535 (min)
        // GetStreamSendWindow(1) returns 5535 (just stream)
        Assert.Equal(65535 - 60000, fc.GetStreamSendWindow(1));
        Assert.Equal(65535 - 60000, fc.GetSendWindow(1));
        Assert.Equal(65535 + 60000 - 60000, fc.ConnectionSendWindow);
    }
}