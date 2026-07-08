using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Semantics;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Semantics;

public sealed class ReconnectPolicySpec
{
    private static TransportOptions MakeTransportOptions() => new TcpTransportOptions
    {
        Host = "example.com",
        Port = 80
    };

    [Fact(Timeout = 5000)]
    public void Start_should_buffer_the_work_and_emit_a_connect_transport()
    {
        var ops = new FakeClientOps();
        var policy = new ReconnectPolicy<string>(ops, maxAttempts: 3);

        policy.Start("buffered-request", MakeTransportOptions());

        Assert.Equal(1, policy.Attempts);
        Assert.Single(ops.Outbound);
        Assert.IsType<ConnectTransport>(ops.Outbound[0]);
    }

    [Fact(Timeout = 5000)]
    public void TakeBuffered_should_return_the_buffered_work_and_reset_the_attempt_counter()
    {
        var ops = new FakeClientOps();
        var policy = new ReconnectPolicy<string>(ops, maxAttempts: 3);

        policy.Start("buffered-request", MakeTransportOptions());
        var buffered = policy.TakeBuffered();

        Assert.Equal("buffered-request", buffered);
        Assert.Equal(0, policy.Attempts);
        Assert.Null(policy.TakeBuffered());
    }

    [Fact(Timeout = 5000)]
    public void OnAttemptFailed_should_retry_and_increment_attempts_below_the_max()
    {
        var ops = new FakeClientOps();
        var policy = new ReconnectPolicy<string>(ops, maxAttempts: 3);
        policy.Start("buffered-request", MakeTransportOptions());

        var exhausted = policy.OnAttemptFailed(MakeTransportOptions(), out var buffered);

        Assert.False(exhausted);
        Assert.Null(buffered);
        Assert.Equal(2, policy.Attempts);
        Assert.Equal(2, ops.Outbound.Count);
        Assert.IsType<ConnectTransport>(ops.Outbound[1]);
    }

    [Fact(Timeout = 5000)]
    public void OnAttemptFailed_should_exhaust_and_disconnect_once_max_attempts_is_reached()
    {
        var ops = new FakeClientOps();
        var policy = new ReconnectPolicy<string>(ops, maxAttempts: 2);
        policy.Start("buffered-request", MakeTransportOptions());

        Assert.False(policy.OnAttemptFailed(MakeTransportOptions(), out _));
        var exhausted = policy.OnAttemptFailed(MakeTransportOptions(), out var buffered);

        Assert.True(exhausted);
        Assert.Equal("buffered-request", buffered);
        Assert.Equal(0, policy.Attempts);
        Assert.IsType<DisconnectTransport>(ops.Outbound[^1]);
    }

    [Fact(Timeout = 5000)]
    public void OnAttemptFailed_should_defer_the_retry_behind_a_backoff_timer_when_backoff_is_configured()
    {
        var ops = new FakeClientOps();
        var policy = new ReconnectPolicy<string>(ops, maxAttempts: 3,
            initialBackoff: TimeSpan.FromMilliseconds(100), maxBackoff: TimeSpan.FromSeconds(5));
        policy.Start("buffered-request", MakeTransportOptions());
        ops.Outbound.Clear();

        var exhausted = policy.OnAttemptFailed(MakeTransportOptions(), out _);

        Assert.False(exhausted);
        // The retry must NOT connect immediately — that is the zero-delay busy-loop we are fixing.
        Assert.DoesNotContain(ops.Outbound, o => o is ConnectTransport);
        var timer = Assert.Single(ops.ScheduledTimers);
        Assert.Equal(ReconnectPolicy<string>.BackoffTimerName, timer.Name);
        Assert.True(timer.Duration > TimeSpan.Zero);
        Assert.True(timer.Duration <= TimeSpan.FromSeconds(5));
    }

    [Fact(Timeout = 5000)]
    public void OnReconnectTimerFired_should_emit_the_connect_transport_for_its_own_timer()
    {
        var ops = new FakeClientOps();
        var policy = new ReconnectPolicy<string>(ops, maxAttempts: 3,
            initialBackoff: TimeSpan.FromMilliseconds(100), maxBackoff: TimeSpan.FromSeconds(5));
        policy.Start("buffered-request", MakeTransportOptions());
        policy.OnAttemptFailed(MakeTransportOptions(), out _);
        ops.Outbound.Clear();

        var handled = policy.OnReconnectTimerFired(ReconnectPolicy<string>.BackoffTimerName);

        Assert.True(handled);
        Assert.Single(ops.Outbound, o => o is ConnectTransport);
    }

    [Fact(Timeout = 5000)]
    public void OnReconnectTimerFired_should_ignore_unrelated_timer_names()
    {
        var ops = new FakeClientOps();
        var policy = new ReconnectPolicy<string>(ops, maxAttempts: 3,
            initialBackoff: TimeSpan.FromMilliseconds(100), maxBackoff: TimeSpan.FromSeconds(5));

        Assert.False(policy.OnReconnectTimerFired("keep-alive-timeout"));
        Assert.Empty(ops.Outbound);
    }

    [Fact(Timeout = 5000)]
    public void OnAttemptFailed_without_backoff_should_connect_immediately()
    {
        var ops = new FakeClientOps();
        var policy = new ReconnectPolicy<string>(ops, maxAttempts: 3);
        policy.Start("buffered-request", MakeTransportOptions());
        ops.Outbound.Clear();

        policy.OnAttemptFailed(MakeTransportOptions(), out _);

        Assert.Single(ops.Outbound, o => o is ConnectTransport);
        Assert.Empty(ops.ScheduledTimers);
    }

    [Fact(Timeout = 5000)]
    public void CanReconnect_should_reflect_whether_max_attempts_is_positive()
    {
        Assert.True(new ReconnectPolicy<string>(new FakeClientOps(), maxAttempts: 1).CanReconnect);
        Assert.False(new ReconnectPolicy<string>(new FakeClientOps(), maxAttempts: 0).CanReconnect);
    }
}
