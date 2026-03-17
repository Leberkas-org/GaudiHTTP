using System.Buffers;
using System.Net;
using System.Threading.Channels;
using Akka.Actor;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using TurboHttp.Client;
using TurboHttp.IO;
using TurboHttp.IO.Stages;

namespace TurboHttp.StreamTests.IO;

/// <summary>
/// Unit tests for <see cref="HostPoolActor.HandleEnsureHost"/> rewrite (TASK-9-011).
/// </summary>
public sealed class HostPoolActorEnsureHostTests : TestKit
{
    private static readonly TcpOptions TestOptions = new() { Host = "localhost", Port = 8080 };

    // ── Helpers ─────────────────────────────────────────────────────────────

    private sealed class FakeConnectionActor : ReceiveActor
    {
        private readonly IActorRef _controlProbe;

        public FakeConnectionActor(IActorRef controlProbe)
        {
            _controlProbe = controlProbe;
        }

        protected override void PreStart()
        {
            _controlProbe.Tell(Self);
        }
    }

    private IActorRef CreatePool(TestProbe controlProbe, RequestEndpoint key, TimeSpan? reconnectInterval = null)
    {
        var config = new TurboClientOptions
        {
            ReconnectInterval = reconnectInterval ?? TimeSpan.FromSeconds(60),
            IdleTimeout = TimeSpan.FromSeconds(60),
            MaxReconnectAttempts = 3
        };

        var hostConfig = new HostPoolActor.HostPoolConfig(
            TestOptions,
            config,
            key,
            ConnectionFactory: () => Props.Create(() => new FakeConnectionActor(controlProbe.Ref)));

        return Sys.ActorOf(Props.Create(() => new HostPoolActor(hostConfig)));
    }

    private static ConnectionHandle CreateHandle(IActorRef connectionActor, RequestEndpoint key)
    {
        var outbound = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        var inbound = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        return new ConnectionHandle(outbound.Writer, inbound.Reader, key, connectionActor);
    }

    private static readonly RequestEndpoint Key11 = new()
    {
        Host = "localhost", Port = 8080, Scheme = "http", Version = HttpVersion.Version11
    };

    private static readonly RequestEndpoint Key10 = new()
    {
        Host = "localhost", Port = 8080, Scheme = "http", Version = HttpVersion.Version10
    };

    // ── EH-001: Slot available → handle returned immediately, MarkBusy called ──

    [Fact(DisplayName = "EH-001: Slot available returns handle immediately and marks connection busy")]
    public void EH_001_SlotAvailable_ReturnsHandleImmediately()
    {
        var controlProbe = CreateTestProbe("control");
        var pool = CreatePool(controlProbe, Key11);

        // PreStart spawns one FakeConnectionActor.
        var fakeConn = controlProbe.ExpectMsg<IActorRef>(TimeSpan.FromSeconds(5));

        // Simulate connection ready.
        var handle = CreateHandle(fakeConn, Key11);
        pool.Tell(new ConnectionActor.ConnectionReady(handle), fakeConn);

        // First EnsureHost → immediate handle reply.
        pool.Tell(new PoolRouterActor.EnsureHost(Key11, TestOptions), TestActor);
        var received = ExpectMsg<ConnectionHandle>(TimeSpan.FromSeconds(5));
        Assert.Equal(handle, received);

        // HTTP/1.1 MaxConcurrentStreams = 6. After MarkBusy once, PendingRequests = 1.
        // Send 5 more to fill to capacity (PendingRequests = 6).
        for (var i = 0; i < 5; i++)
        {
            pool.Tell(new PoolRouterActor.EnsureHost(Key11, TestOptions), TestActor);
            ExpectMsg<ConnectionHandle>(TimeSpan.FromSeconds(5));
        }

        // 7th request — all 6 slots occupied → should be queued (not immediately served).
        pool.Tell(new PoolRouterActor.EnsureHost(Key11, TestOptions), TestActor);
        ExpectNoMsg(TimeSpan.FromMilliseconds(300));
    }

    // ── EH-002: All slots full + under limiter → requester queued, new connection spawned ──

    [Fact(DisplayName = "EH-002: All slots full spawns new connection when under limiter limit")]
    public void EH_002_AllSlotsFull_SpawnsNewConnection()
    {
        var controlProbe = CreateTestProbe("control");
        // HTTP/1.0: MaxConcurrentStreams = 1 → fills with a single MarkBusy.
        var pool = CreatePool(controlProbe, Key10);

        // PreStart spawns connection #1.
        var fakeConn1 = controlProbe.ExpectMsg<IActorRef>(TimeSpan.FromSeconds(5));

        // Make connection ready and occupy its single slot.
        var handle1 = CreateHandle(fakeConn1, Key10);
        pool.Tell(new ConnectionActor.ConnectionReady(handle1), fakeConn1);
        pool.Tell(new PoolRouterActor.EnsureHost(Key10, TestOptions), TestActor);
        ExpectMsg<ConnectionHandle>(TimeSpan.FromSeconds(5));

        // Now all slots are full (HTTP/1.0 max 1). Next request → queued + spawn attempt.
        var requesterProbe = CreateTestProbe("requester");
        pool.Tell(new PoolRouterActor.EnsureHost(Key10, TestOptions), requesterProbe.Ref);

        // SpawnConnection should have been called — a new FakeConnectionActor should appear.
        var fakeConn2 = controlProbe.ExpectMsg<IActorRef>(TimeSpan.FromSeconds(5));
        Assert.NotEqual(fakeConn1, fakeConn2);

        // Requester should NOT have received a reply yet (no handle on conn2).
        requesterProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(200));

        // Simulate the new connection becoming ready.
        var handle2 = CreateHandle(fakeConn2, Key10);
        pool.Tell(new ConnectionActor.ConnectionReady(handle2), fakeConn2);

        // The queued requester should now receive the handle.
        requesterProbe.ExpectMsg<ConnectionHandle>(TimeSpan.FromSeconds(5));
    }

    // ── EH-003: All slots full + at limiter limit → requester queued, no spawn ──

    [Fact(DisplayName = "EH-003: At limiter limit queues requester without spawning")]
    public void EH_003_AtLimiterLimit_QueuesOnly()
    {
        var controlProbe = CreateTestProbe("control");
        // HTTP/1.0: MaxConcurrentStreams = 1 per connection. Limiter default = 6.
        var pool = CreatePool(controlProbe, Key10);

        // PreStart spawns connection #1. Make it ready and fill its slot.
        var conn1 = controlProbe.ExpectMsg<IActorRef>(TimeSpan.FromSeconds(5));
        pool.Tell(new ConnectionActor.ConnectionReady(CreateHandle(conn1, Key10)), conn1);
        pool.Tell(new PoolRouterActor.EnsureHost(Key10, TestOptions), TestActor);
        ExpectMsg<ConnectionHandle>(TimeSpan.FromSeconds(5)); // conn1 now busy

        // Spawn connections 2–6: each EnsureHost finds conn full → queues + spawns.
        // Then make each ready and fill its slot.
        for (var i = 2; i <= 6; i++)
        {
            // This EnsureHost queues a requester and triggers SpawnConnection.
            var probe = CreateTestProbe($"requester-{i}");
            pool.Tell(new PoolRouterActor.EnsureHost(Key10, TestOptions), probe.Ref);

            // New connection spawned.
            var connN = controlProbe.ExpectMsg<IActorRef>(TimeSpan.FromSeconds(5));
            pool.Tell(new ConnectionActor.ConnectionReady(CreateHandle(connN, Key10)), connN);

            // The queued requester gets the handle via HandleConnectionReady.
            probe.ExpectMsg<ConnectionHandle>(TimeSpan.FromSeconds(5));

            // Now conn N has a handle but PendingRequests = 0 (HandleConnectionReady doesn't MarkBusy).
            // Fill its slot via a fresh EnsureHost.
            pool.Tell(new PoolRouterActor.EnsureHost(Key10, TestOptions), CreateTestProbe().Ref);
        }

        // Now: 6 connections, all at capacity (PendingRequests=1, MaxConcurrentStreams=1).
        // Limiter is at capacity (6/6).

        // 7th request — all full AND at limiter limit → queued only, no spawn.
        var finalRequester = CreateTestProbe("final-requester");
        pool.Tell(new PoolRouterActor.EnsureHost(Key10, TestOptions), finalRequester.Ref);

        // Should be queued (no immediate reply).
        finalRequester.ExpectNoMsg(TimeSpan.FromMilliseconds(300));

        // No new connection should be spawned (limiter refuses).
        controlProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }
}
