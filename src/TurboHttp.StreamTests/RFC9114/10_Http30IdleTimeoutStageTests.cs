using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Protocol.RFC9114;
using TurboHttp.Streams.Stages.Decoding;

namespace TurboHttp.StreamTests.RFC9114;

/// <summary>
/// Tests HTTP/3 idle timeout integration in the connection stage per RFC 9114 §5.1.
/// Verifies that <see cref="Http3IdleTimeoutHandler"/> inside <see cref="Http30ConnectionStage"/>
/// sends GOAWAY and completes the stage when idle timeout expires with no active streams.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http30ConnectionStage"/>.
/// RFC 9114 §5.1: An HTTP/3 connection can be idle for some time. Endpoints SHOULD send GOAWAY
/// before closing an idle connection. The idle timeout is reconciled between local and remote values.
/// </remarks>
public sealed class Http30IdleTimeoutStageTests : StreamTestBase
{
    /// <summary>
    /// Runs the Http30ConnectionStage with a specific idle timeout and the given server frames.
    /// Returns (downstream frames from OutApp, server-bound frames from OutServer).
    /// </summary>
    private Task<(IReadOnlyList<Http3Frame> Downstream, IReadOnlyList<Http3Frame> ServerBound)> RunAsync(
        TimeSpan idleTimeout, params Http3Frame[] serverFrames)
        => RunCoreAsync(idleTimeout, keepServerOpen: true, serverFrames);

    private async Task<(IReadOnlyList<Http3Frame> Downstream, IReadOnlyList<Http3Frame> ServerBound)> RunCoreAsync(
        TimeSpan idleTimeout, bool keepServerOpen, Http3Frame[] serverFrames)
    {
        var downstreamSink = Sink.Seq<Http3Frame>();
        var serverBoundSink = Sink.Seq<Http3Frame>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(downstreamSink, serverBoundSink,
                (m1, m2) => (m1, m2),
                (b, dsSink, sbSink) =>
                {
                    var stage = b.Add(new Http30ConnectionStage(idleTimeout));
                    // When keepServerOpen is true, concatenate with Never so the inlet
                    // stays open until the stage itself completes (e.g. idle timeout).
                    var serverSource = b.Add(
                        keepServerOpen
                            ? (serverFrames.Length > 0
                                ? Source.From(serverFrames).Concat(Source.Never<Http3Frame>())
                                : Source.Never<Http3Frame>())
                            : Source.From(serverFrames));
                    var requestSource = b.Add(Source.Never<Http3Frame>());

                    b.From(serverSource).To(stage.InServer);
                    b.From(stage.OutApp).To(dsSink);
                    b.From(requestSource).To(stage.InApp);
                    b.From(stage.OutServer).To(sbSink);

                    return ClosedShape.Instance;
                }));

        var (downstreamTask, serverBoundTask) = graph.Run(Materializer);

        var downstream = await downstreamTask.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var serverBound = await serverBoundTask.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        return (downstream, serverBound);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Idle Timeout Expiry (RFC 9114 §5.1)
    // ──────────────────────────────────────────────────────────────────────

    [Fact(Timeout = 15_000, DisplayName = "RFC9114-5.1-IT-001: Idle timeout sends GOAWAY and completes stage")]
    public async Task Should_SendGoAwayAndComplete_When_IdleTimeoutExpiresWithNoActiveStreams()
    {
        // Use a very short idle timeout so the timer fires quickly.
        var (_, serverBound) = await RunAsync(TimeSpan.FromMilliseconds(200));

        // The stage should emit SETTINGS + MAX_PUSH_ID from PreStart,
        // then GOAWAY when idle timeout expires.
        Assert.Contains(serverBound, f => f is Http3GoAwayFrame);
    }

    [Fact(Timeout = 15_000, DisplayName = "RFC9114-5.1-IT-002: GOAWAY frame has stream ID 0 on idle timeout")]
    public async Task Should_SendGoAwayWithStreamIdZero_When_IdleTimeoutExpires()
    {
        var (_, serverBound) = await RunAsync(TimeSpan.FromMilliseconds(200));

        var goAway = serverBound.OfType<Http3GoAwayFrame>().FirstOrDefault();
        Assert.NotNull(goAway);
        Assert.Equal(0L, goAway.StreamId);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Timeout Disabled (RFC 9114 §5.1)
    // ──────────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-5.1-IT-003: Zero timeout disables idle check — no GOAWAY sent")]
    public async Task Should_NotSendGoAway_When_TimeoutIsZero()
    {
        // TimeSpan.Zero disables idle timeout; feed frames and let source complete.
        var (_, serverBound) = await RunCoreAsync(
            TimeSpan.Zero,
            keepServerOpen: false,
            [new Http3SettingsFrame([]), new Http3DataFrame(new byte[] { 0x01 })]);

        Assert.DoesNotContain(serverBound, f => f is Http3GoAwayFrame);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Constructor Validation
    // ──────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RFC9114-5.1-IT-004: Negative idle timeout throws ArgumentOutOfRangeException")]
    public void Should_ThrowArgumentOutOfRange_When_NegativeTimeout()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Http30ConnectionStage(TimeSpan.FromSeconds(-1)));
    }

    // ──────────────────────────────────────────────────────────────────────
    // Idle Timeout Handler Behavior
    // ──────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RFC9114-5.1-IT-005: ComputeEffectiveTimeout returns minimum of local and remote")]
    public void Should_ReturnMinimum_When_BothTimeoutsProvided()
    {
        var local = TimeSpan.FromSeconds(30);
        var remote = TimeSpan.FromSeconds(15);

        var effective = Http3IdleTimeoutHandler.ComputeEffectiveTimeout(local, remote);

        Assert.Equal(TimeSpan.FromSeconds(15), effective);
    }

    [Fact(DisplayName = "RFC9114-5.1-IT-006: ComputeEffectiveTimeout ignores zero local timeout")]
    public void Should_ReturnRemote_When_LocalTimeoutIsZero()
    {
        var local = TimeSpan.Zero;
        var remote = TimeSpan.FromSeconds(20);

        var effective = Http3IdleTimeoutHandler.ComputeEffectiveTimeout(local, remote);

        Assert.Equal(TimeSpan.FromSeconds(20), effective);
    }
}
