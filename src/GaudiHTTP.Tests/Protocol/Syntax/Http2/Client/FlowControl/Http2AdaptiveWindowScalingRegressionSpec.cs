using Microsoft.Extensions.Time.Testing;
using GaudiHTTP.Protocol.Syntax.Http2;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Client.FlowControl;

/// <summary>
/// Regression guard for HTTP/2 adaptive receive-window scaling (feature/h2-adaptive-window-scaling).
/// Unit specs for the pure <see cref="WindowScaler"/> and basic <see cref="FlowController"/> growth
/// exist already; this spec locks the integration invariants that protect against the flow-control
/// desync class of bug — most importantly that the window credit advertised to the peer (the emitted
/// WINDOW_UPDATE increments) stays exactly consistent with the window the receiver enforces, so a
/// conformant peer that fills the advertised window never trips a false FLOW_CONTROL_ERROR.
/// </summary>
public sealed class Http2AdaptiveWindowScalingRegressionSpec
{
    private const int Start = 64 * 1024;
    private const int Cap = 16 * 1024 * 1024;
    private const int ConnWindow = 64 * 1024 * 1024;

    private static FlowController NewScaling(FakeTimeProvider clock) =>
        new(ConnWindow, Start, new WindowScaler(Cap, 1.0), clock);

    private static void EstablishMinRtt(FlowController fc, FakeTimeProvider clock, int milliseconds)
    {
        fc.OnMeasurementPingSent();
        clock.Advance(TimeSpan.FromMilliseconds(milliseconds));
        fc.OnMeasurementPingAck();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Scaling_growth_increment_should_equal_consumed_bytes_plus_window_delta()
    {
        var clock = new FakeTimeProvider();
        var fc = NewScaling(clock);
        EstablishMinRtt(fc, clock, 100);

        // First saturating round only seeds the sample timestamp (no growth yet).
        fc.OnInboundData(1, Start / 2);
        clock.Advance(TimeSpan.FromMilliseconds(10));

        // Second round grows the window from Start to 2*Start. The emitted increment must replenish
        // the just-consumed bytes (Start/2) AND grant the growth delta (2*Start - Start), so the peer
        // is credited exactly the new window — no over- or under-crediting.
        var result = fc.OnInboundData(1, Start / 2);

        Assert.True(result.Success);
        Assert.Equal(Start * 2, fc.CurrentStreamWindow);
        Assert.NotNull(result.StreamWindowUpdate);
        Assert.Equal(Start / 2 + (Start * 2 - Start), result.StreamWindowUpdate!.Value.Increment);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Scaling_should_grow_monotonically_and_cap_at_max_window()
    {
        var clock = new FakeTimeProvider();
        var fc = NewScaling(clock);
        EstablishMinRtt(fc, clock, 100);

        var previous = fc.CurrentStreamWindow;

        // Drive many saturating rounds; each round delivers exactly the current advertised window.
        for (var round = 0; round < 40; round++)
        {
            var window = fc.CurrentStreamWindow;
            fc.OnInboundData(1, window / 2);
            clock.Advance(TimeSpan.FromMilliseconds(10));
            fc.OnInboundData(1, window - window / 2);

            var current = fc.CurrentStreamWindow;
            Assert.True(current >= previous, "window must never shrink under scaling");
            Assert.True(current <= Cap, "window must never exceed the configured max");
            previous = current;
        }

        Assert.Equal(Cap, fc.CurrentStreamWindow);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Filling_the_advertised_window_each_round_should_never_trigger_a_flow_control_violation()
    {
        // This is the core safety property: whatever window the receiver advertises (CurrentStreamWindow,
        // replenished by the WINDOW_UPDATEs it emits), a peer is entitled to fill it. Doing so must never
        // be classified as a stream or connection violation. A desync between advertised and enforced
        // windows (the preface bug fixed alongside this) would surface here.
        var clock = new FakeTimeProvider();
        var fc = NewScaling(clock);
        EstablishMinRtt(fc, clock, 100);

        for (var round = 0; round < 60; round++)
        {
            var window = fc.CurrentStreamWindow;
            var first = fc.OnInboundData(1, window / 2);
            clock.Advance(TimeSpan.FromMilliseconds(10));
            var second = fc.OnInboundData(1, window - window / 2);

            Assert.True(first.Success, $"round {round}: first half violated flow control");
            Assert.False(first.IsStreamViolation || first.IsConnectionViolation);
            Assert.True(second.Success, $"round {round}: filling the advertised window violated flow control");
            Assert.False(second.IsStreamViolation || second.IsConnectionViolation);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Disabled_scaling_should_keep_a_fixed_window_under_identical_load()
    {
        // Contrast guard: without a scaler the window stays at Start no matter how saturated the link is.
        var fc = new FlowController(ConnWindow, Start);

        for (var round = 0; round < 10; round++)
        {
            fc.OnInboundData(1, Start / 2);
            fc.OnInboundData(1, Start / 2);
            Assert.Equal(Start, fc.CurrentStreamWindow);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void A_new_stream_after_window_scaling_must_be_replenished_within_the_advertised_initial_window()
    {
        // Fast, deterministic UNIT repro for the H2 single-connection large-download deadlock — the
        // mechanism behind the resource-heavy integration repro
        // (GaudiHTTP.IntegrationTests.Client/H2/LargeDownloadRegressionSpec).
        //
        // The adaptive scaler grows the GLOBAL per-stream WINDOW_UPDATE threshold, but a freshly opened
        // stream's *server* send window is still the advertised SETTINGS_INITIAL_WINDOW_SIZE (Start) —
        // we never re-advertise a larger one. If the threshold grew past Start, the new stream's server
        // window is exhausted before the client ever accumulates enough to emit a WINDOW_UPDATE, so the
        // stream deadlocks. No server, no sockets, no concurrency needed to pin it.
        var clock = new FakeTimeProvider();
        var fc = NewScaling(clock);
        EstablishMinRtt(fc, clock, 100);

        // Ratchet the adaptive window (and, before the fix, the shared WU threshold) far above Start.
        for (var round = 0; round < 12; round++)
        {
            var window = fc.CurrentStreamWindow;
            fc.OnInboundData(1, window / 2);
            clock.Advance(TimeSpan.FromMilliseconds(10));
            fc.OnInboundData(1, window - window / 2);
        }

        Assert.True(fc.CurrentStreamWindow >= Start * 4,
            "precondition: the scaler must have grown the window well past the advertised initial");

        // A brand-new stream. Its server send window is the advertised initial (Start), NOT the scaled
        // window. Deliver up to the advertised window in small chunks: a WINDOW_UPDATE MUST be emitted
        // before the advertised window is consumed, or the server stalls and the stream deadlocks.
        const int newStream = 3;
        const int chunk = 16 * 1024;
        var consumed = 0;
        var emittedWindowUpdate = false;
        while (consumed < Start)
        {
            var result = fc.OnInboundData(newStream, chunk);
            Assert.True(result.Success, "delivering within the advertised window must never violate flow control");
            consumed += chunk;
            if (result.StreamWindowUpdate is not null)
            {
                emittedWindowUpdate = true;
                break;
            }
        }

        Assert.True(emittedWindowUpdate,
            $"a new stream must receive a WINDOW_UPDATE within its advertised window ({Start} bytes); " +
            "otherwise the server send window is exhausted before replenishment and the stream deadlocks.");
    }
}
