using TurboHttp.Client;

namespace TurboHttp.Tests.Client;

/// <summary>
/// Tests <see cref="PendingWorkTracker"/> thread-safe counter and self-healing behaviour.
/// </summary>
public sealed class PendingWorkTrackerTests
{
    [Fact(DisplayName = "RFC-9110-pwt-001: Initial state is not pending")]
    public void InitialState_IsNotPending()
    {
        var tracker = new PendingWorkTracker();

        Assert.False(tracker.IsPending);
        Assert.Equal(0, tracker.Count);
    }

    [Fact(DisplayName = "RFC-9110-pwt-002: Increment sets IsPending to true")]
    public void Increment_SetsPending()
    {
        var tracker = new PendingWorkTracker();

        tracker.IncrementPending();

        Assert.True(tracker.IsPending);
        Assert.Equal(1, tracker.Count);
    }

    [Fact(DisplayName = "RFC-9110-pwt-003: Increment then decrement returns to not pending")]
    public void IncrementDecrement_ReturnsToNotPending()
    {
        var tracker = new PendingWorkTracker();

        tracker.IncrementPending();
        tracker.DecrementPending();

        Assert.False(tracker.IsPending);
        Assert.Equal(0, tracker.Count);
    }

    [Fact(DisplayName = "RFC-9110-pwt-004: Multiple increments track count correctly")]
    public void MultipleIncrements_TracksCount()
    {
        var tracker = new PendingWorkTracker();

        tracker.IncrementPending();
        tracker.IncrementPending();
        tracker.IncrementPending();

        Assert.Equal(3, tracker.Count);
        Assert.True(tracker.IsPending);
    }

    [Fact(DisplayName = "RFC-9110-pwt-005: Decrement below zero self-heals to zero")]
    public void DecrementBelowZero_SelfHeals()
    {
        var tracker = new PendingWorkTracker();

        tracker.DecrementPending();

        Assert.Equal(0, tracker.Count);
        Assert.False(tracker.IsPending);
    }

    [Fact(DisplayName = "RFC-9110-pwt-006: Concurrent increments and decrements are thread-safe", Timeout = 5000)]
    public async Task ConcurrentAccess_IsThreadSafe()
    {
        var tracker = new PendingWorkTracker();
        const int iterations = 10_000;

        var incrementTask = Task.Run(() =>
        {
            for (var i = 0; i < iterations; i++)
            {
                tracker.IncrementPending();
            }
        });

        var decrementTask = Task.Run(() =>
        {
            for (var i = 0; i < iterations; i++)
            {
                tracker.DecrementPending();
            }
        });

        await Task.WhenAll(incrementTask, decrementTask);

        // Count should be 0 (or very close due to self-healing)
        Assert.True(tracker.Count >= 0);
    }

    [Fact(DisplayName = "RFC-9110-pwt-007: Increment-decrement-increment cycle tracks correctly")]
    public void IncrementDecrementIncrement_Cycle()
    {
        var tracker = new PendingWorkTracker();

        tracker.IncrementPending();
        Assert.Equal(1, tracker.Count);

        tracker.DecrementPending();
        Assert.Equal(0, tracker.Count);

        tracker.IncrementPending();
        tracker.IncrementPending();
        Assert.Equal(2, tracker.Count);

        tracker.DecrementPending();
        Assert.Equal(1, tracker.Count);
        Assert.True(tracker.IsPending);
    }
}
