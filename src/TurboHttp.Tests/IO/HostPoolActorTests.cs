using System.Buffers;
using System.Net;
using System.Threading.Channels;
using Akka.Actor;
using Akka.Hosting;
using Akka.TestKit.Xunit2;
using TurboHttp.IO;
using TurboHttp.IO.Stages;

namespace TurboHttp.Tests.IO;

public sealed class HostPoolActorTests : TestKit
{
    private static TcpOptions MakeOptions(string host = "test.local", int port = 8080)
        => new() { Host = host, Port = port };

    private static PoolConfig MakeConfig()
        => new(MaxConnectionsPerHost: 5, MaxRequestsPerConnection: 10);

    /// <summary>
    /// Creates a HostPoolActor inside a bidirectional proxy.
    /// Messages sent to the returned proxy ref are forwarded to the HostPoolActor child.
    /// Messages sent by the HostPoolActor to its parent (the proxy) are forwarded to TestActor.
    /// </summary>
    private IActorRef CreateProxy(TcpOptions? options = null, PoolConfig? config = null)
    {
        var opts = options ?? MakeOptions();
        var cfg = config ?? MakeConfig();
        return Sys.ActorOf(Props.Create(() => new HostPoolActorProxy(opts, cfg, TestActor)));
    }

    // ── HA-003: ConnectionHandle returned to requester after TCP connect ────────

    [Fact(DisplayName = "HA-003: ConnectionHandle returned to requester after TCP connect")]
    public async Task HA_003_ConnectionHandleReturnedAfterTcpConnect()
    {
        var clientManagerProbe = CreateTestProbe();
        ActorRegistry.For(Sys).Register<ClientManager>(clientManagerProbe.Ref);

        var proxy = CreateProxy();

        // Send EnsureHost — no active handle yet, so the requester is queued
        var requesterProbe = CreateTestProbe();
        proxy.Tell(new PoolRouterActor.EnsureHost(HostKey.Default, MakeOptions()), requesterProbe.Ref);

        // Capture the CreateTcpRunner from the eagerly spawned connection
        var createMsg = await clientManagerProbe.ExpectMsgAsync<ClientManager.CreateTcpRunner>(TimeSpan.FromSeconds(5));
        var connectionActor = createMsg.Handler;

        // Simulate TCP connected — ConnectionActor sends ConnectionReady to parent (HostPoolActor)
        var inbound = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        var outbound = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        var endpoint = new IPEndPoint(IPAddress.Loopback, 8080);
        var connectedMsg = new ClientRunner.ClientConnected(endpoint, inbound.Reader, outbound.Writer);

        var runnerProbe = CreateTestProbe();
        connectionActor.Tell(connectedMsg, runnerProbe.Ref);

        // The requester should receive the ConnectionHandle
        var handle = await requesterProbe.ExpectMsgAsync<ConnectionHandle>(TimeSpan.FromSeconds(10));
        Assert.NotNull(handle);
        Assert.Equal(HostKey.Default, handle.Key);
        Assert.NotNull(handle.OutboundWriter);
        Assert.NotNull(handle.InboundReader);
    }

    // ── HA-004: Immediate reply when active connection exists ────────────────

    [Fact(DisplayName = "HA-004: Immediate reply when active connection exists")]
    public async Task HA_004_ImmediateReplyWhenActiveConnectionExists()
    {
        var clientManagerProbe = CreateTestProbe();
        ActorRegistry.For(Sys).Register<ClientManager>(clientManagerProbe.Ref);

        var proxy = CreateProxy();

        // Capture the CreateTcpRunner from the eagerly spawned connection
        var createMsg = await clientManagerProbe.ExpectMsgAsync<ClientManager.CreateTcpRunner>(TimeSpan.FromSeconds(5));
        var connectionActor = createMsg.Handler;

        // Simulate TCP connected — establishes active handle in HostPoolActor
        var inbound = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        var outbound = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        var endpoint = new IPEndPoint(IPAddress.Loopback, 8080);
        var connectedMsg = new ClientRunner.ClientConnected(endpoint, inbound.Reader, outbound.Writer);

        var runnerProbe = CreateTestProbe();
        connectionActor.Tell(connectedMsg, runnerProbe.Ref);

        // Wait for HostPoolActor to process ConnectionReady
        await Task.Delay(500);

        // Now send EnsureHost — should get an immediate reply since handle is active
        var requesterProbe = CreateTestProbe();
        proxy.Tell(new PoolRouterActor.EnsureHost(HostKey.Default, MakeOptions()), requesterProbe.Ref);

        var handle = await requesterProbe.ExpectMsgAsync<ConnectionHandle>(TimeSpan.FromSeconds(5));
        Assert.NotNull(handle);
        Assert.Equal(HostKey.Default, handle.Key);
    }

    // ── HA-005: Multiple requesters are all served ──────────────────────────

    [Fact(DisplayName = "HA-005: Multiple requesters are all served")]
    public async Task HA_005_MultipleRequestersAllServed()
    {
        var clientManagerProbe = CreateTestProbe();
        ActorRegistry.For(Sys).Register<ClientManager>(clientManagerProbe.Ref);

        var proxy = CreateProxy();

        // Send EnsureHost from three different requesters — all queued (no active handle)
        var requester1 = CreateTestProbe();
        var requester2 = CreateTestProbe();
        var requester3 = CreateTestProbe();

        proxy.Tell(new PoolRouterActor.EnsureHost(HostKey.Default, MakeOptions()), requester1.Ref);
        proxy.Tell(new PoolRouterActor.EnsureHost(HostKey.Default, MakeOptions()), requester2.Ref);
        proxy.Tell(new PoolRouterActor.EnsureHost(HostKey.Default, MakeOptions()), requester3.Ref);

        // Capture the CreateTcpRunner from the eagerly spawned connection
        var createMsg = await clientManagerProbe.ExpectMsgAsync<ClientManager.CreateTcpRunner>(TimeSpan.FromSeconds(5));
        var connectionActor = createMsg.Handler;

        // Simulate TCP connected
        var inbound = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        var outbound = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        var endpoint = new IPEndPoint(IPAddress.Loopback, 8080);
        var connectedMsg = new ClientRunner.ClientConnected(endpoint, inbound.Reader, outbound.Writer);

        var runnerProbe = CreateTestProbe();
        connectionActor.Tell(connectedMsg, runnerProbe.Ref);

        // All three requesters should receive the ConnectionHandle
        var handle1 = await requester1.ExpectMsgAsync<ConnectionHandle>(TimeSpan.FromSeconds(10));
        var handle2 = await requester2.ExpectMsgAsync<ConnectionHandle>(TimeSpan.FromSeconds(10));
        var handle3 = await requester3.ExpectMsgAsync<ConnectionHandle>(TimeSpan.FromSeconds(10));

        Assert.NotNull(handle1);
        Assert.NotNull(handle2);
        Assert.NotNull(handle3);

        // All should be the same handle instance
        Assert.Equal(handle1, handle2);
        Assert.Equal(handle2, handle3);
    }

    private sealed class HostPoolActorProxy : ReceiveActor
    {
        public HostPoolActorProxy(TcpOptions options, PoolConfig config, IActorRef forwardTo)
        {
            var hostPool = Context.ActorOf(
                Props.Create(() =>
                    new HostPoolActor(new HostPoolActor.HostPoolConfig(options, config, HostKey.Default))),
                "host-pool");

            ReceiveAny(msg =>
            {
                if (Sender.Equals(hostPool))
                {
                    forwardTo.Forward(msg);
                }
                else
                {
                    hostPool.Forward(msg);
                }
            });
        }
    }
}
