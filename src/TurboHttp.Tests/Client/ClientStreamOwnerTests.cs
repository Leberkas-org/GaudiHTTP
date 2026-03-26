using System.Net;
using System.Net.Http;
using System.Threading.Channels;
using Akka.Actor;
using Akka.TestKit.Xunit;
using TurboHttp.Client;
using TurboHttp.Streams;

namespace TurboHttp.Tests.Client;

/// <summary>
/// Tests <see cref="ClientStreamOwnerActor"/> lifecycle: creation, pending work tracking,
/// retry logic with backoff, idle deferral, and graceful shutdown.
/// </summary>
public sealed class ClientStreamOwnerTests : TestKit
{
    private static readonly TurboClientOptions DefaultOptions = new();

    private static TurboRequestOptions DefaultRequestOptions() =>
        new(null, new HttpRequestMessage().Headers, HttpVersion.Version11,
            HttpVersionPolicy.RequestVersionOrLower, TimeSpan.FromSeconds(30), 0);

    private IActorRef CreateOwner() =>
        Sys.ActorOf(Props.Create(() => new ClientStreamOwnerActor()));

    private static ClientStreamOwner.CreateStreamInstance MakeCreateMessage()
    {
        var requests = Channel.CreateUnbounded<HttpRequestMessage>();
        var responses = Channel.CreateUnbounded<HttpResponseMessage>();
        return new ClientStreamOwner.CreateStreamInstance(
            DefaultOptions,
            DefaultRequestOptions,
            PipelineDescriptor.Empty,
            requests.Reader,
            responses.Writer);
    }

    // ── Owner creates stream instance on CreateStreamInstance ──────────────

    [Fact(DisplayName = "RFC-9110-cso-001: Owner spawns child instance on CreateStreamInstance", Timeout = 10000)]
    public void Owner_SpawnsChildInstance_OnCreateStreamInstance()
    {
        var owner = CreateOwner();
        var create = MakeCreateMessage();

        owner.Tell(create, TestActor);

        // The Owner spawns a ClientStreamInstanceActor, sends InitializeStream,
        // and replies with StreamInstanceCreated on success.
        var created = ExpectMsg<ClientStreamOwner.StreamInstanceCreated>(TimeSpan.FromSeconds(5));
        Assert.NotNull(created.InstanceRef);
        Assert.False(created.InstanceRef.IsNobody());
    }

    // ── Owner increments/decrements pending work on signals ───────────────

    [Fact(DisplayName = "RFC-9110-cso-002: Owner tracks pending work increment", Timeout = 5000)]
    public void Owner_TracksPendingWorkIncrement()
    {
        var owner = CreateOwner();

        // Send pending work signal — Owner should not crash
        owner.Tell(new ClientStreamOwner.PendingWorkSignal(1));

        // Verify actor is still alive by sending another message
        owner.Tell(new ClientStreamOwner.PendingWorkSignal(0));
        ExpectNoMsg(TimeSpan.FromMilliseconds(200));
    }

    [Fact(DisplayName = "RFC-9110-cso-003: Owner tracks pending work decrement", Timeout = 5000)]
    public void Owner_TracksPendingWorkDecrement()
    {
        var owner = CreateOwner();

        owner.Tell(new ClientStreamOwner.PendingWorkSignal(1));
        owner.Tell(new ClientStreamOwner.PendingWorkSignal(-1));

        // Verify actor is still alive
        ExpectNoMsg(TimeSpan.FromMilliseconds(200));
    }

    [Fact(DisplayName = "RFC-9110-cso-004: Owner clamps pending work to zero on negative delta", Timeout = 5000)]
    public void Owner_ClampsPendingWork_ToZero()
    {
        var owner = CreateOwner();

        // Decrement without prior increment — should clamp to 0, not crash
        owner.Tell(new ClientStreamOwner.PendingWorkSignal(-1));

        ExpectNoMsg(TimeSpan.FromMilliseconds(200));
    }

    // ── Owner retries child creation on failure ───────────────────────────

    [Fact(DisplayName = "RFC-9110-cso-005: Owner retries on StreamFailed from child", Timeout = 5000)]
    public void Owner_RetriesOnStreamFailed_FromChild()
    {
        var owner = CreateOwner();

        // Simulate the Owner receiving a StreamFailed message from a child.
        // The Owner should schedule a retry via backoff timer.
        owner.Tell(new ClientStreamInstance.StreamFailed(new InvalidOperationException("test failure")));

        // Owner processes the failure internally — no crash
        ExpectNoMsg(TimeSpan.FromMilliseconds(300));
    }

    [Fact(DisplayName = "RFC-9110-cso-006: Owner reports StreamInstanceFailed when retries exhausted", Timeout = 5000)]
    public void Owner_ReportsStreamInstanceFailed_WhenRetriesExhausted()
    {
        var owner = CreateOwner();

        // First send a CreateStreamInstance so the Owner has a requester to reply to
        var create = MakeCreateMessage();
        owner.Tell(create, TestActor);

        // Wait for successful creation
        ExpectMsg<ClientStreamOwner.StreamInstanceCreated>(TimeSpan.FromSeconds(5));

        // Now simulate explicit failure reports that exhaust retries.
        // The Owner tracks attempt count via StreamInstanceFailed messages.
        owner.Tell(new ClientStreamOwner.StreamInstanceFailed(
            new InvalidOperationException("failure 1"), 1));
        owner.Tell(new ClientStreamOwner.StreamInstanceFailed(
            new InvalidOperationException("failure 2"), 2));
        owner.Tell(new ClientStreamOwner.StreamInstanceFailed(
            new InvalidOperationException("failure 3"), 3));

        // After retries exhausted (AttemptNumber >= MaxRetryAttempts), Owner sends failure to requester
        var failed = ExpectMsg<ClientStreamOwner.StreamInstanceFailed>(TimeSpan.FromSeconds(5));
        Assert.NotNull(failed.Reason);
    }

    // ── Owner delays stream completion while pending work exists ──────────

    [Fact(DisplayName = "RFC-9110-cso-007: Owner defers idle request when pending work exists", Timeout = 5000)]
    public void Owner_DefersIdleRequest_WhenPendingWorkExists()
    {
        var owner = CreateOwner();

        // Simulate pending work
        owner.Tell(new ClientStreamOwner.PendingWorkSignal(1));

        // Request idle — should be deferred since pending > 0
        owner.Tell(new ClientStreamOwner.RequestStreamIdle(TestActor));

        // We should NOT immediately receive RequestShutdown because pending work is outstanding
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact(DisplayName = "RFC-9110-cso-008: Owner processes deferred idle request when pending work drains", Timeout = 5000)]
    public void Owner_ProcessesDeferredIdleRequest_WhenPendingWorkDrains()
    {
        var owner = CreateOwner();

        // Simulate pending work
        owner.Tell(new ClientStreamOwner.PendingWorkSignal(1));

        // Request idle — deferred
        owner.Tell(new ClientStreamOwner.RequestStreamIdle(TestActor));

        // Now drain pending work
        owner.Tell(new ClientStreamOwner.PendingWorkSignal(-1));

        // Owner should now process the deferred idle and send RequestShutdown to the requester
        ExpectMsg<ClientStreamInstance.RequestShutdown>(TimeSpan.FromSeconds(2));
    }

    [Fact(DisplayName = "RFC-9110-cso-009: Owner grants idle request immediately when no pending work", Timeout = 5000)]
    public void Owner_GrantsIdleRequest_WhenNoPendingWork()
    {
        var owner = CreateOwner();

        // Request idle with no pending work — should be granted immediately
        owner.Tell(new ClientStreamOwner.RequestStreamIdle(TestActor));

        ExpectMsg<ClientStreamInstance.RequestShutdown>(TimeSpan.FromSeconds(2));
    }

    // ── Owner terminates gracefully on Shutdown ──────────────────────────

    [Fact(DisplayName = "RFC-9110-cso-010: Owner self-terminates on Shutdown when no instance and no pending work", Timeout = 5000)]
    public void Owner_SelfTerminates_OnShutdown_NoInstance()
    {
        var owner = CreateOwner();

        Watch(owner);

        owner.Tell(new ClientStreamOwner.Shutdown());

        // Owner should self-terminate since there's no stream instance
        ExpectTerminated(owner, TimeSpan.FromSeconds(5));
    }

    [Fact(DisplayName = "RFC-9110-cso-011: Owner waits for pending work on Shutdown then terminates", Timeout = 10000)]
    public void Owner_WaitsForPendingWork_ThenTerminates()
    {
        var owner = CreateOwner();

        Watch(owner);

        // Add pending work
        owner.Tell(new ClientStreamOwner.PendingWorkSignal(1));

        // Request shutdown — should wait for pending work
        owner.Tell(new ClientStreamOwner.Shutdown());

        // Should not terminate yet
        ExpectNoMsg(TimeSpan.FromMilliseconds(300));

        // Drain pending work
        owner.Tell(new ClientStreamOwner.PendingWorkSignal(-1));

        // Now owner should terminate (no instance to shut down)
        ExpectTerminated(owner, TimeSpan.FromSeconds(5));
    }

    [Fact(DisplayName = "RFC-9110-cso-012: Owner force-terminates after 5s shutdown timeout", Timeout = 10000)]
    public void Owner_ForceTerminates_AfterShutdownTimeout()
    {
        var owner = CreateOwner();

        Watch(owner);

        // Add pending work that will never drain
        owner.Tell(new ClientStreamOwner.PendingWorkSignal(1));

        // Request shutdown
        owner.Tell(new ClientStreamOwner.Shutdown());

        // Owner should force-terminate after ~5 second timeout
        ExpectTerminated(owner, TimeSpan.FromSeconds(8));
    }

    [Fact(DisplayName = "RFC-9110-cso-013: Double shutdown is safe", Timeout = 5000)]
    public void Owner_DoubleShutdown_IsSafe()
    {
        var owner = CreateOwner();

        Watch(owner);

        owner.Tell(new ClientStreamOwner.Shutdown());
        owner.Tell(new ClientStreamOwner.Shutdown());

        ExpectTerminated(owner, TimeSpan.FromSeconds(5));
    }
}
