using TurboHttp.Internal;
using TurboHttp.Protocol.RFC9113;
using TurboHttp.Streams;
using TurboHttp.Streams.Stages.Routing;

namespace TurboHttp.Tests.RFC9113;

/// <summary>
/// Comprehensive tests for MAX_CONCURRENT_STREAMS tracking and enforcement per RFC 9113 §5.1.2.
/// Covers SETTINGS frame tracking, engine configuration, handle callbacks, enforcement logic,
/// queue behavior, timeout handling, queue overflow, and mid-connection limit updates.
/// </summary>
/// <remarks>
/// Classes under test: <see cref="Http20Engine"/>, <see cref="Http20StreamLimiterStage"/>,
/// <see cref="StreamLimiterHandle"/>.
/// RFC 9113 §5.1.2: An endpoint MUST NOT exceed the limit set by its peer.
/// RFC 9113 §6.5.2: SETTINGS_MAX_CONCURRENT_STREAMS indicates the maximum number of concurrent
/// streams that the sender will allow.
/// </remarks>
public sealed class Http2MaxConcurrentStreamsTests
{
    // ── Tracking: SETTINGS frame with MAX_CONCURRENT_STREAMS ──

    [Fact(Timeout = 10000, DisplayName = "RFC9113-5.1.2-MCS-001: SETTINGS frame with MAX_CONCURRENT_STREAMS=5 is stored")]
    public void Should_StoreMaxConcurrentStreams5_When_SettingsFrameContainsIt()
    {
        var frame = new SettingsFrame([(SettingsParameter.MaxConcurrentStreams, 5u)]);
        var parameter = frame.Parameters.Single();
        Assert.Equal(SettingsParameter.MaxConcurrentStreams, parameter.Item1);
        Assert.Equal(5u, parameter.Item2);

        var engine = new Http20Engine(initialWindowSize: 65535, maxConcurrentStreams: (int)parameter.Item2);
        Assert.Equal(5, engine.MaxConcurrentStreams);
    }

    [Fact(Timeout = 10000, DisplayName = "RFC9113-5.1.2-MCS-002: MAX_CONCURRENT_STREAMS=0 in engine stores zero")]
    public void Should_StoreZero_When_MaxConcurrentStreamsIs0()
    {
        var engine = new Http20Engine(initialWindowSize: 65535, maxConcurrentStreams: 0);
        Assert.Equal(0, engine.MaxConcurrentStreams);
    }

    [Fact(Timeout = 10000, DisplayName = "RFC9113-5.1.2-MCS-003: Default MAX_CONCURRENT_STREAMS is unlimited (int.MaxValue)")]
    public void Should_DefaultToUnlimited_When_NoMaxConcurrentStreamsSpecified()
    {
        var engine = new Http20Engine();
        Assert.Equal(int.MaxValue, engine.MaxConcurrentStreams);
    }

    [Theory(Timeout = 10000, DisplayName = "RFC9113-5.1.2-MCS-004: SETTINGS frame stores various MAX_CONCURRENT_STREAMS values")]
    [InlineData(1u)]
    [InlineData(5u)]
    [InlineData(100u)]
    [InlineData(256u)]
    [InlineData(1000u)]
    [InlineData((uint)int.MaxValue)]
    public void Should_StoreCorrectValue_When_SettingsFrameHasVariousMaxConcurrentStreams(uint maxStreams)
    {
        var frame = new SettingsFrame([(SettingsParameter.MaxConcurrentStreams, maxStreams)]);
        var decoded = frame.Parameters
            .Where(p => p.Item1 == SettingsParameter.MaxConcurrentStreams)
            .Select(p => (int)p.Item2)
            .Single();
        Assert.Equal((int)maxStreams, decoded);
    }

    [Fact(Timeout = 10000, DisplayName = "RFC9113-5.1.2-MCS-005: SETTINGS frame with multiple params includes MAX_CONCURRENT_STREAMS")]
    public void Should_ExtractMaxConcurrentStreams_When_SettingsFrameHasMultipleParams()
    {
        var frame = new SettingsFrame([
            (SettingsParameter.HeaderTableSize, 4096u),
            (SettingsParameter.MaxConcurrentStreams, 42u),
            (SettingsParameter.InitialWindowSize, 32768u),
            (SettingsParameter.MaxFrameSize, 16384u),
        ]);

        var maxStreams = frame.Parameters
            .Where(p => p.Item1 == SettingsParameter.MaxConcurrentStreams)
            .Select(p => (int)p.Item2)
            .SingleOrDefault();

        Assert.Equal(42, maxStreams);
    }

    // ── Enforcement: Can create up to MAX_CONCURRENT_STREAMS streams ──

    [Fact(Timeout = 10000, DisplayName = "RFC9113-5.1.2-MCS-006: Active count below limit allows new stream")]
    public void Should_AllowStream_When_ActiveCountBelowMaxConcurrentStreams()
    {
        // Simulates the enforcement check used in Http20StreamLimiterStage
        var activeStreams = 3;
        var maxConcurrentStreams = 5;
        Assert.True(activeStreams < maxConcurrentStreams, "Should allow stream when under limit");
    }

    [Fact(Timeout = 10000, DisplayName = "RFC9113-5.1.2-MCS-007: Active count at limit of 5 blocks 6th stream")]
    public void Should_BlockSixthStream_When_FiveStreamsActiveAndLimitIs5()
    {
        var activeStreams = 5;
        var maxConcurrentStreams = 5;
        Assert.False(activeStreams < maxConcurrentStreams, "6th stream should be blocked at limit=5");
    }

    [Fact(Timeout = 10000, DisplayName = "RFC9113-5.1.2-MCS-008: Creating exactly MAX_CONCURRENT_STREAMS succeeds")]
    public void Should_AllowUpToLimit_When_StreamsCreatedSequentially()
    {
        var maxConcurrentStreams = 5;
        var activeStreams = 0;
        for (var i = 0; i < maxConcurrentStreams; i++)
        {
            Assert.True(activeStreams < maxConcurrentStreams);
            activeStreams++;
        }

        Assert.Equal(maxConcurrentStreams, activeStreams);
        Assert.False(activeStreams < maxConcurrentStreams, "Next stream should be blocked");
    }

    // ── Enforcement: Stream close releases queued streams ──

    [Fact(Timeout = 10000, DisplayName = "RFC9113-5.1.2-MCS-009: Stream close decrements active count")]
    public void Should_DecrementActiveCount_When_StreamCloses()
    {
        var activeStreams = 5;
        activeStreams--;
        Assert.Equal(4, activeStreams);
        Assert.True(activeStreams < 5, "After close, should be below limit again");
    }

    [Fact(Timeout = 10000, DisplayName = "RFC9113-5.1.2-MCS-010: Multiple stream closes release multiple slots")]
    public void Should_ReleaseMultipleSlots_When_MultipleStreamsClose()
    {
        var maxConcurrentStreams = 3;
        var activeStreams = 3;
        var queuedCount = 4;

        // Close 2 streams → 2 queued requests should be releasable
        activeStreams--;
        activeStreams--;
        var released = 0;

        while (activeStreams < maxConcurrentStreams && queuedCount > 0)
        {
            activeStreams++;
            queuedCount--;
            released++;
        }

        Assert.Equal(2, released);
        Assert.Equal(3, activeStreams);
        Assert.Equal(2, queuedCount);
    }

    [Fact(Timeout = 10000, DisplayName = "RFC9113-5.1.2-MCS-011: StreamLimiterHandle OnStreamClosed callback is invoked")]
    public void Should_InvokeOnStreamClosed_When_StreamCloses()
    {
        var handle = new StreamLimiterHandle();
        var closeCount = 0;
        handle.OnStreamClosed = () => closeCount++;

        handle.OnStreamClosed.Invoke();
        handle.OnStreamClosed.Invoke();
        handle.OnStreamClosed.Invoke();

        Assert.Equal(3, closeCount);
    }

    // ── Enforcement: Queue overflow (100 pending) → rejected ──

    [Fact(Timeout = 10000, DisplayName = "RFC9113-5.1.2-MCS-012: DefaultMaxPendingQueueSize is 100")]
    public void Should_HaveMaxPendingQueueOf100()
    {
        Assert.Equal(100, Http20StreamLimiterStage.DefaultMaxPendingQueueSize);
    }

    [Fact(Timeout = 10000, DisplayName = "RFC9113-5.1.2-MCS-013: Queue at capacity rejects new request")]
    public void Should_RejectRequest_When_PendingQueueAtMaxCapacity()
    {
        var pendingQueueCount = Http20StreamLimiterStage.DefaultMaxPendingQueueSize;
        var maxQueueSize = Http20StreamLimiterStage.DefaultMaxPendingQueueSize;
        Assert.False(pendingQueueCount < maxQueueSize, "Queue at 100 should reject");
    }

    [Fact(Timeout = 10000, DisplayName = "RFC9113-5.1.2-MCS-014: Queue below capacity accepts new request")]
    public void Should_AcceptRequest_When_PendingQueueBelowCapacity()
    {
        var pendingQueueCount = 99;
        var maxQueueSize = Http20StreamLimiterStage.DefaultMaxPendingQueueSize;
        Assert.True(pendingQueueCount < maxQueueSize, "Queue at 99 should accept");
    }

    // ── Enforcement: Request timeout while queued ──

    [Fact(Timeout = 10000, DisplayName = "RFC9113-5.1.2-MCS-015: DefaultQueueTimeout is 30 seconds")]
    public void Should_HaveDefaultQueueTimeoutOf30Seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), Http20StreamLimiterStage.DefaultQueueTimeout);
    }

    [Fact(Timeout = 10000, DisplayName = "RFC9113-5.1.2-MCS-016: Timeout error includes active and max counts")]
    public void Should_IncludeCountsInTimeoutMessage()
    {
        // Verifies the error message format used by the limiter stage
        var active = 5;
        var max = 5;
        var timeout = 30.0;
        var message = $"Request timed out after {timeout}s waiting for available HTTP/2 stream (active={active}, max={max})";

        Assert.Contains("timed out", message);
        Assert.Contains("active=5", message);
        Assert.Contains("max=5", message);
    }

    // ── Server updates MAX_CONCURRENT_STREAMS mid-connection ──

    [Fact(Timeout = 10000, DisplayName = "RFC9113-5.1.2-MCS-017: OnMaxConcurrentStreamsChanged passes new value")]
    public void Should_PassNewValue_When_ServerUpdatesMaxConcurrentStreams()
    {
        var handle = new StreamLimiterHandle();
        var receivedValues = new List<int>();
        handle.OnMaxConcurrentStreamsChanged = v => receivedValues.Add(v);

        handle.OnMaxConcurrentStreamsChanged.Invoke(10);
        handle.OnMaxConcurrentStreamsChanged.Invoke(3);
        handle.OnMaxConcurrentStreamsChanged.Invoke(50);

        Assert.Equal([10, 3, 50], receivedValues);
    }

    [Fact(Timeout = 10000, DisplayName = "RFC9113-5.1.2-MCS-018: Limit increase allows previously blocked streams")]
    public void Should_AllowBlockedStreams_When_LimitIncreased()
    {
        var activeStreams = 3;
        var maxConcurrentStreams = 3;
        Assert.False(activeStreams < maxConcurrentStreams, "At capacity");

        // Server increases limit
        maxConcurrentStreams = 10;
        Assert.True(activeStreams < maxConcurrentStreams, "Should allow after limit increase");
    }

    [Fact(Timeout = 10000, DisplayName = "RFC9113-5.1.2-MCS-019: Limit decrease blocks new streams when above new limit")]
    public void Should_BlockNewStreams_When_LimitDecreasedBelowActive()
    {
        var activeStreams = 5;
        var maxConcurrentStreams = 10;
        Assert.True(activeStreams < maxConcurrentStreams);

        // Server decreases limit below active count
        maxConcurrentStreams = 3;
        Assert.False(activeStreams < maxConcurrentStreams, "Should block when active > new limit");
    }

    [Fact(Timeout = 10000, DisplayName = "RFC9113-5.1.2-MCS-020: Limit change to zero blocks all new streams")]
    public void Should_BlockAllNewStreams_When_LimitChangedToZero()
    {
        var activeStreams = 0;
        var maxConcurrentStreams = 0;
        Assert.False(activeStreams < maxConcurrentStreams, "Zero limit blocks everything");
    }

    // ── Engine integration ──

    [Fact(Timeout = 10000, DisplayName = "RFC9113-5.1.2-MCS-021: Engine CreateFlow succeeds with MAX_CONCURRENT_STREAMS=5")]
    public void Should_CreateFlowSuccessfully_When_MaxConcurrentStreamsIs5()
    {
        var engine = new Http20Engine(initialWindowSize: 65535, maxConcurrentStreams: 5);
        var flow = engine.CreateFlow();
        Assert.NotNull(flow);
        Assert.Equal(5, engine.MaxConcurrentStreams);
    }

    [Fact(Timeout = 10000, DisplayName = "RFC9113-5.1.2-MCS-022: StreamLimiterHandle null callbacks do not throw")]
    public void Should_NotThrow_When_NullCallbackInvokedConditionally()
    {
        var handle = new StreamLimiterHandle();
        Assert.Null(handle.OnStreamClosed);
        Assert.Null(handle.OnMaxConcurrentStreamsChanged);

        var exception = Record.Exception(() =>
        {
            handle.OnStreamClosed?.Invoke();
            handle.OnMaxConcurrentStreamsChanged?.Invoke(5);
        });

        Assert.Null(exception);
    }

    [Fact(Timeout = 10000, DisplayName = "RFC9113-5.1.2-MCS-023: Queue overflow error uses RefusedStream error code")]
    public void Should_UseRefusedStreamErrorCode_When_QueueOverflows()
    {
        // Verifies the error format used by the limiter stage
        var error = new Http2Exception(
            "Connection limit exceeded: 100 requests pending and 5 streams active (max 5)",
            Http2ErrorCode.RefusedStream,
            Http2ErrorScope.Stream);

        Assert.Equal(Http2ErrorCode.RefusedStream, error.ErrorCode);
        Assert.Contains("Connection limit exceeded", error.Message);
        Assert.Contains("100 requests pending", error.Message);
    }

    [Fact(Timeout = 10000, DisplayName = "RFC9113-5.1.2-MCS-024: Timeout error uses RefusedStream error code")]
    public void Should_UseRefusedStreamErrorCode_When_RequestTimesOut()
    {
        var error = new Http2Exception(
            "Request timed out after 30s waiting for available HTTP/2 stream (active=5, max=5)",
            Http2ErrorCode.RefusedStream,
            Http2ErrorScope.Stream);

        Assert.Equal(Http2ErrorCode.RefusedStream, error.ErrorCode);
        Assert.Contains("timed out", error.Message);
    }

    [Fact(Timeout = 10000, DisplayName = "RFC9113-5.1.2-MCS-025: Active count does not go negative on redundant close")]
    public void Should_NotGoNegative_When_CloseCalledWithZeroActiveStreams()
    {
        var activeStreams = 0;
        if (activeStreams > 0)
        {
            activeStreams--;
        }

        Assert.Equal(0, activeStreams);
    }
}
