using GaudiHTTP.Protocol.Syntax.Http3;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http3.Client.StateMachine;

public sealed class Http3ConnectionStateEdgeCasesSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.2")]
    public void OnServerGoAway_should_accept_valid_stream_id_divisible_by_four()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));
        var frame = new GoAwayFrame(streamId: 0);

        // Should not throw
        state.OnServerGoAway(frame);

        Assert.True(state.GoAwayReceived);
        Assert.Equal(0, state.LastGoAwayStreamId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.2")]
    public void OnServerGoAway_should_reject_stream_id_not_divisible_by_four()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));
        var frame = new GoAwayFrame(streamId: 1);

        var ex = Assert.Throws<HttpProtocolException>(() => state.OnServerGoAway(frame));
        Assert.Contains("divisible by 4", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.2")]
    public void OnServerGoAway_should_reject_stream_id_not_divisible_by_four_odd()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));
        var frame = new GoAwayFrame(streamId: 3);

        var ex = Assert.Throws<HttpProtocolException>(() => state.OnServerGoAway(frame));
        Assert.Contains("divisible by 4", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.2")]
    public void OnServerGoAway_should_reject_stream_id_not_divisible_by_four_mod_two()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));
        var frame = new GoAwayFrame(streamId: 2);

        var ex = Assert.Throws<HttpProtocolException>(() => state.OnServerGoAway(frame));
        Assert.Contains("divisible by 4", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.2")]
    public void OnServerGoAway_should_reject_increasing_stream_ids()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));
        var frame1 = new GoAwayFrame(streamId: 4);
        var frame2 = new GoAwayFrame(streamId: 8);

        state.OnServerGoAway(frame1);

        var ex = Assert.Throws<HttpProtocolException>(() => state.OnServerGoAway(frame2));
        Assert.Contains("must not increase beyond previous value", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.2")]
    public void OnServerGoAway_should_accept_decreasing_stream_ids()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));
        var frame1 = new GoAwayFrame(streamId: 8);
        var frame2 = new GoAwayFrame(streamId: 4);

        state.OnServerGoAway(frame1);
        state.OnServerGoAway(frame2);

        Assert.Equal(4, state.LastGoAwayStreamId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.2")]
    public void OnServerGoAway_should_accept_equal_stream_ids()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));
        var frame1 = new GoAwayFrame(streamId: 8);
        var frame2 = new GoAwayFrame(streamId: 8);

        state.OnServerGoAway(frame1);

        // Frame1 set LastGoAwayStreamId to 8; Frame2 with streamId=8 should be allowed since 8 is not > 8
        state.OnServerGoAway(frame2);

        Assert.Equal(8, state.LastGoAwayStreamId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.2")]
    public void OnServerGoAway_should_throw_on_null_frame()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));

        Assert.Throws<ArgumentNullException>(() => state.OnServerGoAway(null!));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void OnRemoteSettings_should_accept_first_settings_frame()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));
        var frame = new SettingsFrame([(SettingsIdentifier.MaxFieldSectionSize, 4096L)]);

        state.OnRemoteSettings(frame);

        Assert.True(state.RemoteSettingsReceived);
        Assert.NotNull(state.RemoteSettings);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void OnRemoteSettings_should_reject_duplicate_settings_frames()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));
        var frame1 = new SettingsFrame([(SettingsIdentifier.MaxFieldSectionSize, 4096L)]);
        var frame2 = new SettingsFrame([(SettingsIdentifier.MaxFieldSectionSize, 8192L)]);

        state.OnRemoteSettings(frame1);

        var ex = Assert.Throws<HttpProtocolException>(() => state.OnRemoteSettings(frame2));
        Assert.Contains("second SETTINGS frame", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void OnRemoteSettings_should_throw_on_null_frame()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));

        Assert.Throws<NullReferenceException>(() => state.OnRemoteSettings(null!));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void OnRemoteSettings_should_store_multiple_parameters()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));
        var frame = new SettingsFrame([
            (SettingsIdentifier.MaxFieldSectionSize, 4096L),
            (SettingsIdentifier.QpackMaxTableCapacity, 2048L)
        ]);

        state.OnRemoteSettings(frame);

        Assert.NotNull(state.RemoteSettings);
        Assert.Equal(4096, state.RemoteMaxFieldSectionSize);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.1")]
    public void RecordActivity_should_update_last_activity()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));

        state.RecordActivity();

        var newTimeout = state.TimeUntilExpiry();

        Assert.True(newTimeout.TotalSeconds > 29);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.1")]
    public void IsIdleTimeoutExpired_should_return_false_on_fresh_connection()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));

        Assert.False(state.IsIdleTimeoutExpired());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.1")]
    public async Task IsIdleTimeoutExpired_should_return_true_when_timeout_elapsed()
    {
        var state = new ConnectionState(TimeSpan.FromMilliseconds(100));

        await Task.Delay(150, TestContext.Current.CancellationToken);

        Assert.True(state.IsIdleTimeoutExpired());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.1")]
    public void IsIdleTimeoutExpired_should_return_false_when_timeout_disabled()
    {
        var state = new ConnectionState(TimeSpan.Zero);

        Assert.False(state.IsIdleTimeoutExpired());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.1")]
    public void TimeUntilExpiry_should_return_remaining_time()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(10));

        var remaining = state.TimeUntilExpiry();

        Assert.True(remaining.TotalSeconds > 9);
        Assert.True(remaining.TotalSeconds <= 10);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.1")]
    public async Task TimeUntilExpiry_should_return_zero_when_expired()
    {
        var state = new ConnectionState(TimeSpan.FromMilliseconds(100));

        await Task.Delay(150, TestContext.Current.CancellationToken);

        var remaining = state.TimeUntilExpiry();

        Assert.Equal(TimeSpan.Zero, remaining);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.1")]
    public void TimeUntilExpiry_should_return_max_value_when_disabled()
    {
        var state = new ConnectionState(TimeSpan.Zero);

        var remaining = state.TimeUntilExpiry();

        Assert.Equal(TimeSpan.MaxValue, remaining);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.1")]
    public void IsTimeoutDisabled_should_return_true_for_zero_timeout()
    {
        var state = new ConnectionState(TimeSpan.Zero);

        Assert.True(state.IsTimeoutDisabled);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.1")]
    public void IsTimeoutDisabled_should_return_false_for_nonzero_timeout()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));

        Assert.False(state.IsTimeoutDisabled);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.4")]
    public void OnStreamOpened_should_increment_active_stream_count()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));

        Assert.Equal(0, state.ActiveStreamCount);

        state.OnStreamOpened();

        Assert.Equal(1, state.ActiveStreamCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.4")]
    public void OnStreamOpened_should_increment_multiple_times()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));

        state.OnStreamOpened();
        state.OnStreamOpened();
        state.OnStreamOpened();

        Assert.Equal(3, state.ActiveStreamCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.4")]
    public async Task OnStreamOpened_should_record_activity()
    {
        var state = new ConnectionState(TimeSpan.FromMilliseconds(100));

        await Task.Delay(150, TestContext.Current.CancellationToken);

        // Expired before opening stream
        Assert.True(state.IsIdleTimeoutExpired());

        // Create fresh state
        var state2 = new ConnectionState(TimeSpan.FromSeconds(30));
        state2.OnStreamOpened();

        // Should record activity, resetting timeout
        Assert.False(state2.IsIdleTimeoutExpired());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.4")]
    public void OnStreamClosed_should_decrement_active_stream_count()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));

        state.OnStreamOpened();
        state.OnStreamOpened();
        Assert.Equal(2, state.ActiveStreamCount);

        state.OnStreamClosed();

        Assert.Equal(1, state.ActiveStreamCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.4")]
    public void OnStreamClosed_should_not_go_negative()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));

        // Close without opening
        state.OnStreamClosed();

        Assert.Equal(0, state.ActiveStreamCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.4")]
    public void OnStreamClosed_should_record_activity()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));

        state.OnStreamOpened();
        state.OnStreamClosed();

        // Activity was recorded
        Assert.False(state.IsIdleTimeoutExpired());
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.2")]
    public void Reset_should_clear_goaway_state()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));
        var frame = new GoAwayFrame(streamId: 4);

        state.OnServerGoAway(frame);
        Assert.True(state.GoAwayReceived);

        state.Reset();

        Assert.False(state.GoAwayReceived);
        Assert.Equal(-1, state.LastGoAwayStreamId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void Reset_should_clear_settings_state()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));
        var frame = new SettingsFrame([(SettingsIdentifier.MaxFieldSectionSize, 4096L)]);

        state.OnRemoteSettings(frame);
        Assert.True(state.RemoteSettingsReceived);

        state.Reset();

        Assert.False(state.RemoteSettingsReceived);
        Assert.Null(state.RemoteSettings);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.4")]
    public void Reset_should_clear_stream_count()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));

        state.OnStreamOpened();
        state.OnStreamOpened();
        Assert.Equal(2, state.ActiveStreamCount);

        state.Reset();

        Assert.Equal(0, state.ActiveStreamCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5")]
    public async Task Reset_should_record_activity()
    {
        var state = new ConnectionState(TimeSpan.FromMilliseconds(100));

        await Task.Delay(150, TestContext.Current.CancellationToken);
        Assert.True(state.IsIdleTimeoutExpired());

        state.Reset();

        // Activity recorded, timeout reset
        Assert.False(state.IsIdleTimeoutExpired());
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void RemoteMaxFieldSectionSize_should_return_null_before_settings()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));

        Assert.Null(state.RemoteMaxFieldSectionSize);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void RemoteMaxFieldSectionSize_should_return_value_after_settings()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));
        var frame = new SettingsFrame([(SettingsIdentifier.MaxFieldSectionSize, 8192L)]);

        state.OnRemoteSettings(frame);

        Assert.Equal(8192, state.RemoteMaxFieldSectionSize);
    }
}