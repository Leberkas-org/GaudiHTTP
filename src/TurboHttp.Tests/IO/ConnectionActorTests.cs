using System.Buffers;
using System.Net;
using System.Threading.Channels;
using Akka.Actor;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using TurboHttp.IO;
using TurboHttp.IO.Stages;

namespace TurboHttp.Tests.IO;

public sealed class ConnectionActorTests : TestKit
{
    private static TcpOptions MakeOptions()
        => new() { Host = "test.local", Port = 8080 };

    /// <summary>
    /// Creates a ConnectionActor as a top-level actor with TestActor as the clientManager.
    /// Context.Parent will be the user guardian — suitable for tests that don't inspect
    /// parent-bound messages.
    /// </summary>
    private IActorRef CreateConnectionActor(TcpOptions? options = null)
    {
        var opts = options ?? MakeOptions();
        return Sys.ActorOf(Props.Create(() => new ConnectionActor(opts, TestActor)));
    }

    /// <summary>
    /// Creates a ConnectionActor as a child of a forwarder proxy.
    /// Returns the clientManager probe and the ConnectionActor's self ref
    /// (extracted from the first CreateTcpRunner message).
    /// The forwarder forwards all child→parent messages to TestActor.
    /// </summary>
    private async Task<(IActorRef connectionActor, TestProbe clientManagerProbe)>
        CreateConnectionActorWithParent(TcpOptions? options = null)
    {
        var opts = options ?? MakeOptions();
        var cmProbe = CreateTestProbe();
        Sys.ActorOf(Props.Create(() => new ConnectionActorParent(opts, cmProbe.Ref, TestActor)));

        var create = await cmProbe.ExpectMsgAsync<ClientManager.CreateTcpRunner>(TimeSpan.FromSeconds(3));
        return (create.Handler, cmProbe);
    }

    /// <summary>
    /// Creates inbound/outbound channels matching the ClientRunner.ClientConnected shape.
    /// </summary>
    private static (
        Channel<(IMemoryOwner<byte>, int)> inbound,
        Channel<(IMemoryOwner<byte>, int)> outbound,
        ClientRunner.ClientConnected msg
        ) MakeConnectedMessage()
    {
        var inbound = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        var outbound = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        var endpoint = new IPEndPoint(IPAddress.Loopback, 8080);
        var msg = new ClientRunner.ClientConnected(endpoint, inbound.Reader, outbound.Writer);
        return (inbound, outbound, msg);
    }

    // ── Connection lifecycle ──────────────────────────────────────────

    [Fact(DisplayName = "CA-001: PreStart sends CreateTcpRunner with correct TcpOptions and Self")]
    public void CA_001_PreStart_SendsCreateTcpRunner()
    {
        var options = MakeOptions();
        var actor = CreateConnectionActor(options);

        var create = ExpectMsg<ClientManager.CreateTcpRunner>(TimeSpan.FromSeconds(3));

        Assert.Equal(options.Host, create.Options.Host);
        Assert.Equal(options.Port, create.Options.Port);
        Assert.Equal(actor, create.Handler);
    }

    [Fact(DisplayName = "CA-004: ClientDisconnected triggers reconnect (sends CreateTcpRunner again)")]
    public void CA_004_ClientDisconnected_TriggersReconnect()
    {
        var actor = CreateConnectionActor();
        var firstCreate = ExpectMsg<ClientManager.CreateTcpRunner>();

        // Simulate connection
        var (_, _, connMsg) = MakeConnectedMessage();
        actor.Tell(connMsg, TestActor);

        // Simulate disconnection
        var endpoint = new IPEndPoint(IPAddress.Loopback, 8080);
        actor.Tell(new ClientRunner.ClientDisconnected(endpoint));

        // Should receive a second CreateTcpRunner (reconnect)
        var secondCreate = ExpectMsg<ClientManager.CreateTcpRunner>(TimeSpan.FromSeconds(5));
        Assert.Equal(firstCreate.Options.Host, secondCreate.Options.Host);
        Assert.Equal(firstCreate.Options.Port, secondCreate.Options.Port);
    }

    [Fact(DisplayName = "CA-005: Terminated of runner triggers reconnect")]
    public void CA_005_Terminated_OfRunner_TriggersReconnect()
    {
        var actor = CreateConnectionActor();
        ExpectMsg<ClientManager.CreateTcpRunner>();

        // Create a probe to act as the runner so we can terminate it
        var runnerProbe = CreateTestProbe();

        var (_, _, connMsg) = MakeConnectedMessage();

        // Send ClientConnected from the runner probe (so _runner = runnerProbe)
        actor.Tell(connMsg, runnerProbe);

        // Stop the runner probe — ConnectionActor watches it, so it gets Terminated
        Sys.Stop(runnerProbe);

        // Should receive a new CreateTcpRunner (reconnect)
        var reconnect = ExpectMsg<ClientManager.CreateTcpRunner>(TimeSpan.FromSeconds(5));
        Assert.NotNull(reconnect);
    }

    [Fact(DisplayName = "CA-006: Reconnect sends new ConnectionReady with fresh ConnectionHandle")]
    public async Task CA_006_Reconnect_SendsNewConnectionReady()
    {
        var (connectionActor, cmProbe) = await CreateConnectionActorWithParent();

        // First connection
        var (_, _, connMsg1) = MakeConnectedMessage();
        connectionActor.Tell(connMsg1, cmProbe.Ref);
        var ready1 = await ExpectMsgAsync<ConnectionActor.ConnectionReady>(TimeSpan.FromSeconds(3));

        // Disconnect
        var endpoint = new IPEndPoint(IPAddress.Loopback, 8080);
        connectionActor.Tell(new ClientRunner.ClientDisconnected(endpoint));

        // Expect reconnect → new CreateTcpRunner
        await cmProbe.ExpectMsgAsync<ClientManager.CreateTcpRunner>(TimeSpan.FromSeconds(5));

        // Second connection
        var (_, _, connMsg2) = MakeConnectedMessage();
        connectionActor.Tell(connMsg2, cmProbe.Ref);
        var ready2 = await ExpectMsgAsync<ConnectionActor.ConnectionReady>(TimeSpan.FromSeconds(3));

        // New handle should be different (new channels)
        Assert.NotSame(ready1.Handle, ready2.Handle);
        Assert.Equal(connectionActor, ready2.Handle.ConnectionActor);
    }

    // ── Cleanup (PostStop) ───────────────────────────────────────────

    [Fact(DisplayName = "CA-012: PostStop sends DoClose to runner")]
    public void CA_012_PostStop_SendsDoCloseToRunner()
    {
        var actor = CreateConnectionActor();
        ExpectMsg<ClientManager.CreateTcpRunner>();

        // Use a probe as the runner so we can verify it receives DoClose
        var runnerProbe = CreateTestProbe();
        var (_, _, connMsg) = MakeConnectedMessage();
        actor.Tell(connMsg, runnerProbe);

        // Stop the actor — PostStop should send DoClose to the runner
        Sys.Stop(actor);

        runnerProbe.ExpectMsg<DoClose>(TimeSpan.FromSeconds(5));
    }

    [Fact(DisplayName = "CA-014: PostStop with null runner does not throw")]
    public void CA_014_PostStop_NullRunner_NoThrow()
    {
        var actor = CreateConnectionActor();
        ExpectMsg<ClientManager.CreateTcpRunner>();

        // Do NOT send ClientConnected — _runner stays null
        // Stop the actor — PostStop should not throw
        Watch(actor);
        Sys.Stop(actor);

        // Actor should terminate without issues
        ExpectTerminated(actor, TimeSpan.FromSeconds(3));
    }

    // ── ConnectionReady with ConnectionHandle ────────────────────────

    [Fact(DisplayName = "CA-019: ClientConnected tells parent ConnectionReady with valid ConnectionHandle")]
    public async Task CA_019_ClientConnected_TellsParentConnectionReady()
    {
        var (connectionActor, cmProbe) = await CreateConnectionActorWithParent();

        var (_, _, connMsg) = MakeConnectedMessage();
        connectionActor.Tell(connMsg, cmProbe.Ref);

        var ready = await ExpectMsgAsync<ConnectionActor.ConnectionReady>(TimeSpan.FromSeconds(3));

        Assert.NotNull(ready.Handle);
        Assert.NotNull(ready.Handle.OutboundWriter);
        Assert.NotNull(ready.Handle.InboundReader);
        Assert.Equal(connectionActor, ready.Handle.ConnectionActor);
    }

    [Fact(DisplayName = "CA-020: ConnectionHandle channels are functional (write -> read roundtrip)")]
    public async Task CA_020_ConnectionHandle_ChannelsAreFunctional()
    {
        var (connectionActor, cmProbe) = await CreateConnectionActorWithParent();

        var (inbound, outbound, connMsg) = MakeConnectedMessage();
        connectionActor.Tell(connMsg, cmProbe.Ref);

        var ready = await ExpectMsgAsync<ConnectionActor.ConnectionReady>(TimeSpan.FromSeconds(3));

        var handle = ready.Handle;

        // Verify outbound: write via handle -> read from outbound channel
        var outMem = MemoryPool<byte>.Shared.Rent(4);
        outMem.Memory.Span[0] = 0xAB;
        await handle.OutboundWriter.WriteAsync((outMem, 4));

        using var cts1 = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(3));
        var (readMem, readLen) = await outbound.Reader.ReadAsync(cts1.Token);
        Assert.Equal(4, readLen);
        Assert.Equal(0xAB, readMem.Memory.Span[0]);
        readMem.Dispose();

        // Verify inbound: write to inbound channel -> read via handle
        var inMem = MemoryPool<byte>.Shared.Rent(4);
        inMem.Memory.Span[0] = 0xCD;
        await inbound.Writer.WriteAsync((inMem, 4));

        using var cts2 = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(3));
        var (handleMem, handleLen) = await handle.InboundReader.ReadAsync(cts2.Token);
        Assert.Equal(4, handleLen);
        Assert.Equal(0xCD, handleMem.Memory.Span[0]);
        handleMem.Dispose();
    }

    [Fact(DisplayName = "CA-021: ConnectionActor does not handle DataItem messages")]
    public async Task CA_021_DataItem_NotHandled()
    {
        var (connectionActor, cmProbe) = await CreateConnectionActorWithParent();

        var (_, outbound, connMsg) = MakeConnectedMessage();
        connectionActor.Tell(connMsg, cmProbe.Ref);

        await ExpectMsgAsync<ConnectionActor.ConnectionReady>(TimeSpan.FromSeconds(3));

        // Send a DataItem — ConnectionActor should not process it (no handler)
        var owner = MemoryPool<byte>.Shared.Rent(4);
        var item = new DataItem(HostKey.Default, owner, 4);
        connectionActor.Tell(item);

        // Nothing should appear in the outbound channel
        await Task.Delay(200);
        Assert.False(outbound.Reader.TryRead(out _));

        owner.Dispose();
    }

    // ── Helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Proxy actor that spawns a ConnectionActor as its child and forwards all
    /// messages received from the child (via Context.Parent.Tell) to <paramref name="forwardTo"/>.
    /// This allows TestActor to intercept parent-bound messages in unit tests.
    /// </summary>
    private sealed class ConnectionActorParent : ReceiveActor
    {
        public ConnectionActorParent(TcpOptions options, IActorRef clientManager, IActorRef forwardTo)
        {
            Context.ActorOf(Props.Create(() => new ConnectionActor(options, clientManager)), "connection");
            ReceiveAny(msg => forwardTo.Forward(msg));
        }
    }
}
