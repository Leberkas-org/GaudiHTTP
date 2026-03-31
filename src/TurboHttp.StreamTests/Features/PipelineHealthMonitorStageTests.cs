using System.Diagnostics.Metrics;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Diagnostics;
using TurboHttp.Streams.Stages.Features;

namespace TurboHttp.StreamTests.Features;

/// <summary>
/// Tests for <see cref="PipelineHealthMonitorStage{T}"/> — production-ready stall detection stage.
/// Verifies transparent pass-through, stall detection with configurable timeout,
/// OTel metric emission, and disabled-monitoring mode.
/// </summary>
public sealed class PipelineHealthMonitorStageTests : StreamTestBase
{
    [Fact(Timeout = 10_000,
        DisplayName = "MONITOR-001: Pass-through does not alter elements")]
    public async Task Should_PassThroughElements_Without_Alteration()
    {
        var input = Enumerable.Range(1, 100).ToList();

        var stage = new PipelineHealthMonitorStage<int>(
            stallTimeout: TimeSpan.FromSeconds(10),
            stageName: "Test");

        var result = await Source.From(input)
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.Seq<int>(), Materializer);

        Assert.Equal(input, result);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "MONITOR-002: Stall fires callback after configured timeout")]
    public async Task Should_FireCallback_When_StallExceedsTimeout()
    {
        var stallDetected = new TaskCompletionSource<(string Stage, string Direction, TimeSpan Duration)>();

        var stage = new PipelineHealthMonitorStage<int>(
            stallTimeout: TimeSpan.FromMilliseconds(500),
            stageName: "StallTest",
            stallCallback: (s, d, dur) => stallDetected.TrySetResult((s, d, dur)));

        // Source.Queue lets us control exactly when elements flow.
        var (queue, sinkTask) = Source.Queue<int>(16, OverflowStrategy.Backpressure)
            .Via(Flow.FromGraph(stage))
            .ToMaterialized(Sink.Seq<int>(), Keep.Both)
            .Run(Materializer);

        // Push one element through, then stall.
        await queue.OfferAsync(1);

        // Wait for the stall callback (with generous timeout for CI)
        var (reportedStage, reportedDirection, reportedDuration) =
            await stallDetected.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal("StallTest", reportedStage);
        Assert.Equal("in", reportedDirection);
        Assert.True(reportedDuration >= TimeSpan.FromMilliseconds(400),
            $"Expected stall duration >= 400ms, got {reportedDuration.TotalMilliseconds:F0}ms");

        // Tear down
        queue.Complete();
    }

    [Fact(Timeout = 10_000,
        DisplayName = "MONITOR-003: Stall counter increments in OTel meter")]
    public async Task Should_IncrementOTelCounter_When_StallDetected()
    {
        long stallCount = 0;

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == TurboHttpMetrics.MeterName
                && instrument.Name == "turbohttp.pipeline.stall")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, _, _) =>
        {
            Interlocked.Add(ref stallCount, measurement);
        });
        listener.Start();

        var stage = new PipelineHealthMonitorStage<int>(
            stallTimeout: TimeSpan.FromMilliseconds(500),
            stageName: "OTelTest");

        var (queue, sinkTask) = Source.Queue<int>(16, OverflowStrategy.Backpressure)
            .Via(Flow.FromGraph(stage))
            .ToMaterialized(Sink.Seq<int>(), Keep.Both)
            .Run(Materializer);

        // Push one element then stall
        await queue.OfferAsync(1);

        // Wait enough time for at least one stall event to fire
        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        // Flush any pending measurements
        listener.RecordObservableInstruments();

        Assert.True(Interlocked.Read(ref stallCount) >= 1,
            $"Expected at least 1 stall counter increment, got {stallCount}");

        // Tear down
        queue.Complete();
    }

    [Fact(Timeout = 10_000,
        DisplayName = "MONITOR-004: TimeSpan.Zero disables monitoring -- no callback fired")]
    public async Task Should_NotFireCallback_When_MonitoringDisabled()
    {
        var callbackFired = false;

        var stage = new PipelineHealthMonitorStage<int>(
            stallTimeout: TimeSpan.Zero,
            stageName: "DisabledTest",
            stallCallback: (_, _, _) => callbackFired = true);

        var (queue, sinkTask) = Source.Queue<int>(16, OverflowStrategy.Backpressure)
            .Via(Flow.FromGraph(stage))
            .ToMaterialized(Sink.Seq<int>(), Keep.Both)
            .Run(Materializer);

        // Push one element then stall
        await queue.OfferAsync(1);

        // Wait well beyond any reasonable timer period
        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.False(callbackFired, "Callback should not fire when monitoring is disabled");

        // Tear down
        queue.Complete();
    }

    [Fact(Timeout = 10_000,
        DisplayName = "MONITOR-005: Empty source completes stage cleanly")]
    public async Task Should_CompleteCleanly_When_SourceIsEmpty()
    {
        var stage = new PipelineHealthMonitorStage<int>(
            stallTimeout: TimeSpan.FromMilliseconds(500),
            stageName: "EmptyTest");

        var result = await Source.Empty<int>()
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.Seq<int>(), Materializer);

        Assert.Empty(result);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "MONITOR-006: Upstream failure propagates through stage")]
    public async Task Should_PropagateUpstreamFailure()
    {
        var stage = new PipelineHealthMonitorStage<int>(
            stallTimeout: TimeSpan.FromSeconds(10),
            stageName: "FailTest");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await Source.Failed<int>(new InvalidOperationException("boom"))
                .Via(Flow.FromGraph(stage))
                .RunWith(Sink.Seq<int>(), Materializer);
        });
    }
}
