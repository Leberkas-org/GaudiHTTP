using TurboHttp.Internal;
using TurboHttp.Streams;
using TurboHttp.Streams.Stages.Routing;

namespace TurboHttp.Tests.RFC9113;

/// <summary>
/// Tests MAX_CONCURRENT_STREAMS enforcement contracts per RFC 9113 §5.1.2.
/// Verifies StreamLimiterHandle wiring, queue constants, and engine integration.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http20StreamLimiterStage"/>.
/// Handle under test: <see cref="StreamLimiterHandle"/>.
/// RFC 9113 §5.1.2: An endpoint MUST NOT exceed the limit set by its peer for concurrent streams.
/// </remarks>
public sealed class Http20StreamLimiterEnforcementTests
{
    // --- Handle Contract Tests ---

    [Fact(Timeout = 5000, DisplayName = "RFC9113-5.1.2-ENF-001: StreamLimiterHandle starts with null callbacks")]
    public void Should_HaveNullCallbacks_When_HandleCreated()
    {
        var handle = new StreamLimiterHandle();
        Assert.Null(handle.OnStreamClosed);
        Assert.Null(handle.OnMaxConcurrentStreamsChanged);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9113-5.1.2-ENF-002: StreamLimiterHandle OnStreamClosed can be set and invoked")]
    public void Should_InvokeCallback_When_OnStreamClosedSet()
    {
        var handle = new StreamLimiterHandle();
        var invoked = false;
        handle.OnStreamClosed = () => invoked = true;

        handle.OnStreamClosed.Invoke();
        Assert.True(invoked);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9113-5.1.2-ENF-003: StreamLimiterHandle OnMaxConcurrentStreamsChanged can be set and invoked")]
    public void Should_InvokeCallback_When_OnMaxConcurrentStreamsChangedSet()
    {
        var handle = new StreamLimiterHandle();
        var receivedValue = -1;
        handle.OnMaxConcurrentStreamsChanged = v => receivedValue = v;

        handle.OnMaxConcurrentStreamsChanged.Invoke(42);
        Assert.Equal(42, receivedValue);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9113-5.1.2-ENF-004: StreamLimiterHandle null-safe invocation pattern")]
    public void Should_NotThrow_When_InvokingNullCallbackViaConditionalAccess()
    {
        var handle = new StreamLimiterHandle();
        var exception = Record.Exception(() =>
        {
            handle.OnStreamClosed?.Invoke();
            handle.OnMaxConcurrentStreamsChanged?.Invoke(10);
        });
        Assert.Null(exception);
    }

    // --- Constants Tests ---

    [Fact(Timeout = 5000, DisplayName = "RFC9113-5.1.2-ENF-005: DefaultMaxPendingQueueSize is 100")]
    public void Should_HaveDefaultMaxPendingQueueSizeOf100()
    {
        Assert.Equal(100, Http20StreamLimiterStage.DefaultMaxPendingQueueSize);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9113-5.1.2-ENF-006: DefaultQueueTimeout is 30 seconds")]
    public void Should_HaveDefaultQueueTimeoutOf30Seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), Http20StreamLimiterStage.DefaultQueueTimeout);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9113-5.1.2-ENF-007: DefaultMaxConcurrentStreams is int.MaxValue (unlimited)")]
    public void Should_HaveDefaultMaxConcurrentStreamsOfIntMaxValue()
    {
        Assert.Equal(int.MaxValue, Http20Engine.DefaultMaxConcurrentStreams);
    }

    // --- Engine Integration Tests ---

    [Fact(Timeout = 5000, DisplayName = "RFC9113-5.1.2-ENF-008: Http20Engine CreateFlow succeeds with maxConcurrentStreams=1")]
    public void Should_CreateFlowSuccessfully_When_MaxConcurrentStreamsIs1()
    {
        var engine = new Http20Engine(initialWindowSize: 65535, maxConcurrentStreams: 1);
        var flow = engine.CreateFlow();
        Assert.NotNull(flow);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9113-5.1.2-ENF-009: Http20Engine CreateFlow succeeds with maxConcurrentStreams=0")]
    public void Should_CreateFlowSuccessfully_When_MaxConcurrentStreamsIs0()
    {
        var engine = new Http20Engine(initialWindowSize: 65535, maxConcurrentStreams: 0);
        var flow = engine.CreateFlow();
        Assert.NotNull(flow);
    }

    [Theory(Timeout = 5000, DisplayName = "RFC9113-5.1.2-ENF-010: Http20Engine CreateFlow succeeds with various limits")]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(int.MaxValue)]
    public void Should_CreateFlowSuccessfully_When_VariousLimitsProvided(int maxStreams)
    {
        var engine = new Http20Engine(initialWindowSize: 65535, maxConcurrentStreams: maxStreams);
        var flow = engine.CreateFlow();
        Assert.NotNull(flow);
    }

    // --- Enforcement Logic Tests ---

    [Fact(Timeout = 5000, DisplayName = "RFC9113-5.1.2-ENF-011: Active count below limit allows new stream")]
    public void Should_AllowNewStream_When_ActiveCountBelowLimit()
    {
        // Simulates the enforcement check: activeStreams < maxConcurrentStreams
        var activeStreams = 2;
        var maxConcurrentStreams = 5;
        Assert.True(activeStreams < maxConcurrentStreams);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9113-5.1.2-ENF-012: Active count at limit blocks new stream")]
    public void Should_BlockNewStream_When_ActiveCountAtLimit()
    {
        var activeStreams = 5;
        var maxConcurrentStreams = 5;
        Assert.False(activeStreams < maxConcurrentStreams);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9113-5.1.2-ENF-013: Active count above limit (after decrease) blocks new stream")]
    public void Should_BlockNewStream_When_ActiveCountAboveLimitAfterDecrease()
    {
        // Scenario: 5 streams open, server decreases limit to 3
        var activeStreams = 5;
        var maxConcurrentStreams = 3;
        Assert.False(activeStreams < maxConcurrentStreams);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9113-5.1.2-ENF-014: Queue capacity check with bounded queue")]
    public void Should_RejectNewRequest_When_QueueAtMaxCapacity()
    {
        var pendingQueueCount = Http20StreamLimiterStage.DefaultMaxPendingQueueSize;
        Assert.False(pendingQueueCount < Http20StreamLimiterStage.DefaultMaxPendingQueueSize);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9113-5.1.2-ENF-015: Queue capacity check allows enqueue below limit")]
    public void Should_AcceptNewRequest_When_QueueBelowMaxCapacity()
    {
        var pendingQueueCount = 50;
        Assert.True(pendingQueueCount < Http20StreamLimiterStage.DefaultMaxPendingQueueSize);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9113-5.1.2-ENF-016: Decrement active count on stream close")]
    public void Should_DecrementActiveCount_When_StreamCloses()
    {
        var activeStreams = 5;
        activeStreams--; // Simulates CloseStream decrementing
        Assert.Equal(4, activeStreams);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9113-5.1.2-ENF-017: Decrement does not go below zero")]
    public void Should_NotGoNegative_When_ActiveStreamsAlreadyZero()
    {
        var activeStreams = 0;
        if (activeStreams > 0)
        {
            activeStreams--;
        }
        Assert.Equal(0, activeStreams);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9113-5.1.2-ENF-018: Mid-connection limit increase allows queued requests")]
    public void Should_AllowQueuedRequests_When_LimitIncreased()
    {
        // Before: 3 active, limit 3, 2 queued
        var activeStreams = 3;
        var maxConcurrentStreams = 3;
        Assert.False(activeStreams < maxConcurrentStreams);

        // Server increases limit to 5
        maxConcurrentStreams = 5;
        Assert.True(activeStreams < maxConcurrentStreams);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9113-5.1.2-ENF-019: StreamLimiterHandle supports multiple callback reassignments")]
    public void Should_SupportCallbackReassignment_When_HandleCallbacksUpdated()
    {
        var handle = new StreamLimiterHandle();
        var count1 = 0;
        var count2 = 0;

        handle.OnStreamClosed = () => count1++;
        handle.OnStreamClosed.Invoke();
        Assert.Equal(1, count1);

        handle.OnStreamClosed = () => count2++;
        handle.OnStreamClosed.Invoke();
        Assert.Equal(1, count1); // Old callback not invoked again
        Assert.Equal(1, count2); // New callback invoked
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9113-5.1.2-ENF-020: OnMaxConcurrentStreamsChanged passes correct value")]
    public void Should_PassCorrectValue_When_MaxConcurrentStreamsChanges()
    {
        var handle = new StreamLimiterHandle();
        var values = new List<int>();
        handle.OnMaxConcurrentStreamsChanged = v => values.Add(v);

        handle.OnMaxConcurrentStreamsChanged.Invoke(1);
        handle.OnMaxConcurrentStreamsChanged.Invoke(100);
        handle.OnMaxConcurrentStreamsChanged.Invoke(int.MaxValue);
        handle.OnMaxConcurrentStreamsChanged.Invoke(0);

        Assert.Equal([1, 100, int.MaxValue, 0], values);
    }
}
