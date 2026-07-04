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
    public void CanReconnect_should_reflect_whether_max_attempts_is_positive()
    {
        Assert.True(new ReconnectPolicy<string>(new FakeClientOps(), maxAttempts: 1).CanReconnect);
        Assert.False(new ReconnectPolicy<string>(new FakeClientOps(), maxAttempts: 0).CanReconnect);
    }
}
