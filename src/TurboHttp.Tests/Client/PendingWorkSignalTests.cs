using Akka.Actor;
using Akka.TestKit.Xunit;
using TurboHttp.Client;

namespace TurboHttp.Tests.Client;

/// <summary>
/// Tests the pending-work signaling protocol between feature BidiStages and
/// the <see cref="ClientStreamOwnerActor"/>. Verifies that increment/decrement
/// patterns from Retry, Cache, and Redirect stages correctly coordinate with
/// the Owner's pending work counter.
/// </summary>
public sealed class PendingWorkSignalTests : TestKit
{
    private IActorRef CreateOwner() =>
        Sys.ActorOf(Props.Create(() => new ClientStreamOwnerActor()));

    // ── RetryBidiStage increment on deciding to retry ─────────────────────

    [Fact(DisplayName = "RFC-9110-pws-001: Retry increment signal prevents premature idle", Timeout = 5000)]
    public void RetryIncrement_PreventsPrematureIdle()
    {
        var owner = CreateOwner();

        // Simulate RetryBidiStage incrementing pending work before re-injecting a request
        owner.Tell(new ClientStreamOwner.PendingWorkSignal(1));

        // Request idle — should be deferred because retry is in-flight
        owner.Tell(new ClientStreamOwner.RequestStreamIdle(TestActor));

        // Owner should NOT send RequestShutdown while retry is pending
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact(DisplayName = "RFC-9110-pws-002: Retry decrement after completion allows idle", Timeout = 5000)]
    public void RetryDecrement_AfterCompletion_AllowsIdle()
    {
        var owner = CreateOwner();

        // Simulate retry: increment when deciding to retry
        owner.Tell(new ClientStreamOwner.PendingWorkSignal(1));

        // Request idle — deferred
        owner.Tell(new ClientStreamOwner.RequestStreamIdle(TestActor));

        // Simulate retry completion: decrement
        owner.Tell(new ClientStreamOwner.PendingWorkSignal(-1));

        // Now idle should be granted
        ExpectMsg<ClientStreamInstance.RequestShutdown>(TimeSpan.FromSeconds(2));
    }

    [Fact(DisplayName = "RFC-9110-pws-003: Multiple retry increments require equal decrements", Timeout = 5000)]
    public void MultipleRetryIncrements_RequireEqualDecrements()
    {
        var owner = CreateOwner();

        // Simulate 3 concurrent retries
        owner.Tell(new ClientStreamOwner.PendingWorkSignal(1));
        owner.Tell(new ClientStreamOwner.PendingWorkSignal(1));
        owner.Tell(new ClientStreamOwner.PendingWorkSignal(1));

        // Request idle — deferred
        owner.Tell(new ClientStreamOwner.RequestStreamIdle(TestActor));

        // Complete 2 of 3 — still pending
        owner.Tell(new ClientStreamOwner.PendingWorkSignal(-1));
        owner.Tell(new ClientStreamOwner.PendingWorkSignal(-1));

        ExpectNoMsg(TimeSpan.FromMilliseconds(300));

        // Complete last one — idle granted
        owner.Tell(new ClientStreamOwner.PendingWorkSignal(-1));

        ExpectMsg<ClientStreamInstance.RequestShutdown>(TimeSpan.FromSeconds(2));
    }

    // ── CacheBidiStage increment on cache revalidation ────────────────────

    [Fact(DisplayName = "RFC-9110-pws-004: Cache revalidation increment prevents premature idle", Timeout = 5000)]
    public void CacheRevalidationIncrement_PreventsPrematureIdle()
    {
        var owner = CreateOwner();

        // Simulate CacheBidiStage incrementing pending work for revalidation
        owner.Tell(new ClientStreamOwner.PendingWorkSignal(1));

        owner.Tell(new ClientStreamOwner.RequestStreamIdle(TestActor));

        // Should not idle while revalidation is in-flight
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact(DisplayName = "RFC-9110-pws-005: Cache revalidation decrement after 304 allows idle", Timeout = 5000)]
    public void CacheRevalidationDecrement_After304_AllowsIdle()
    {
        var owner = CreateOwner();

        // Simulate revalidation: increment
        owner.Tell(new ClientStreamOwner.PendingWorkSignal(1));

        owner.Tell(new ClientStreamOwner.RequestStreamIdle(TestActor));

        // Simulate 304 response received: decrement
        owner.Tell(new ClientStreamOwner.PendingWorkSignal(-1));

        ExpectMsg<ClientStreamInstance.RequestShutdown>(TimeSpan.FromSeconds(2));
    }

    // ── Pending work counter reflects accurate count ──────────────────────

    [Fact(DisplayName = "RFC-9110-pws-006: PendingWorkTracker reflects accurate count across increment/decrement cycles", Timeout = 5000)]
    public void Tracker_ReflectsAccurateCount_AcrossCycles()
    {
        var tracker = new PendingWorkTracker();

        // Simulate retry stage: increment before re-injection
        tracker.IncrementPending();
        Assert.Equal(1, tracker.Count);
        Assert.True(tracker.IsPending);

        // Simulate cache stage: increment for revalidation
        tracker.IncrementPending();
        Assert.Equal(2, tracker.Count);

        // Retry completes
        tracker.DecrementPending();
        Assert.Equal(1, tracker.Count);
        Assert.True(tracker.IsPending);

        // Cache revalidation completes
        tracker.DecrementPending();
        Assert.Equal(0, tracker.Count);
        Assert.False(tracker.IsPending);
    }

    [Fact(DisplayName = "RFC-9110-pws-007: PendingWorkTracker handles interleaved retry and cache signals", Timeout = 5000)]
    public void Tracker_HandlesInterleavedSignals()
    {
        var tracker = new PendingWorkTracker();

        // Retry starts
        tracker.IncrementPending();
        // Cache revalidation starts
        tracker.IncrementPending();
        // Another retry starts
        tracker.IncrementPending();
        Assert.Equal(3, tracker.Count);

        // First retry completes
        tracker.DecrementPending();
        // Cache completes
        tracker.DecrementPending();
        Assert.Equal(1, tracker.Count);
        Assert.True(tracker.IsPending);

        // Second retry completes
        tracker.DecrementPending();
        Assert.Equal(0, tracker.Count);
        Assert.False(tracker.IsPending);
    }

    [Fact(DisplayName = "RFC-9110-pws-008: PendingWorkTracker self-heals on spurious decrement", Timeout = 5000)]
    public void Tracker_SelfHeals_OnSpuriousDecrement()
    {
        var tracker = new PendingWorkTracker();

        // Spurious decrement (e.g., duplicate completion callback)
        tracker.DecrementPending();

        // Should self-heal to 0, not go negative
        Assert.Equal(0, tracker.Count);
        Assert.False(tracker.IsPending);

        // Subsequent operations should work normally
        tracker.IncrementPending();
        Assert.Equal(1, tracker.Count);
        Assert.True(tracker.IsPending);
    }

    // ── Mixed signal protocol with Owner actor ────────────────────────────

    [Fact(DisplayName = "RFC-9110-pws-009: Mixed retry and cache signals coordinate shutdown correctly", Timeout = 5000)]
    public void MixedSignals_CoordinateShutdown()
    {
        var owner = CreateOwner();

        Watch(owner);

        // Simulate retry in-flight
        owner.Tell(new ClientStreamOwner.PendingWorkSignal(1));
        // Simulate cache revalidation in-flight
        owner.Tell(new ClientStreamOwner.PendingWorkSignal(1));

        // Request shutdown — should wait for both
        owner.Tell(new ClientStreamOwner.Shutdown());

        // Still pending
        ExpectNoMsg(TimeSpan.FromMilliseconds(300));

        // Retry completes
        owner.Tell(new ClientStreamOwner.PendingWorkSignal(-1));
        ExpectNoMsg(TimeSpan.FromMilliseconds(100));

        // Cache completes — now all pending work is done
        owner.Tell(new ClientStreamOwner.PendingWorkSignal(-1));

        // Owner should now self-terminate (no stream instance)
        ExpectTerminated(owner, TimeSpan.FromSeconds(5));
    }

    [Fact(DisplayName = "RFC-9110-pws-010: Bulk delta signal adjusts pending count atomically", Timeout = 5000)]
    public void BulkDelta_AdjustsPendingCount()
    {
        var owner = CreateOwner();

        // Send a bulk increment (e.g., batch of retries)
        owner.Tell(new ClientStreamOwner.PendingWorkSignal(3));

        owner.Tell(new ClientStreamOwner.RequestStreamIdle(TestActor));

        // Not idle yet
        ExpectNoMsg(TimeSpan.FromMilliseconds(300));

        // Bulk decrement
        owner.Tell(new ClientStreamOwner.PendingWorkSignal(-3));

        // Now idle
        ExpectMsg<ClientStreamInstance.RequestShutdown>(TimeSpan.FromSeconds(2));
    }
}
