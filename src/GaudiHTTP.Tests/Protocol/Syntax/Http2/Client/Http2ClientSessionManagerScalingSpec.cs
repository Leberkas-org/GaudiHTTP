using Akka.Actor;
using Akka.Event;
using Microsoft.Extensions.Time.Testing;
using Servus.Akka.Transport;
using GaudiHTTP.Client;
using GaudiHTTP.Protocol.Syntax.Http2;
using GaudiHTTP.Protocol.Syntax.Http2.Client;
using GaudiHTTP.Streams.Stages.Client;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Client;

public sealed class Http2ClientSessionManagerScalingSpec
{
    private sealed class FakeClientStageOperations : IClientStageOperations
    {
        public List<Http2Frame> EmittedFrames { get; } = [];

        public void OnResponse(HttpResponseMessage response) { }

        public void OnOutbound(ITransportOutbound item)
        {
            if (item is TransportData { Buffer: var buf })
            {
                var decoder = new FrameDecoder();
                var frames = decoder.Decode(buf);
                EmittedFrames.AddRange(frames);
            }
        }

        public void OnScheduleTimer(string name, TimeSpan duration) { }

        public void OnCancelTimer(string name) { }

        public ILoggingAdapter Log => throw new NotImplementedException();

        public IActorRef StageActor => throw new NotImplementedException();
    }

    [Fact(Timeout = 5000)]
    public void Session_should_emit_measurement_ping_on_inbound_data_when_scaling_enabled()
    {
        var clock = new FakeTimeProvider();
        var options = new GaudiClientOptions
        {
            Http2 = new Http2ClientOptions
            {
                InitialStreamWindowSize = 64 * 1024,
                MaxStreamWindowSize = 1024 * 1024,
                WindowScaleThresholdMultiplier = 1.0,
                EnableAdaptiveWindowScaling = true
            }
        };

        var ops = new FakeClientStageOperations();
        var sm = new Http2ClientSessionManager(options, ops, clock);

        // Trigger a measurement PING by processing an inbound DATA frame.
        var dataFrame = new DataFrame(streamId: 1, data: new byte[100], endStream: false);
        sm.ProcessFrame(dataFrame);

        // Expect a PING frame to be emitted.
        var pings = ops.EmittedFrames.OfType<PingFrame>().ToList();
        Assert.NotEmpty(pings);

        // Verify it's a measurement PING (sentinel payload).
        var measurementPing = pings.First(Http2ClientSessionManager.IsRttPing);
        Assert.NotNull(measurementPing);
    }

    [Fact(Timeout = 5000)]
    public void Session_should_record_minrtt_when_measurement_ping_ack_received()
    {
        var clock = new FakeTimeProvider();
        var options = new GaudiClientOptions
        {
            Http2 = new Http2ClientOptions
            {
                InitialStreamWindowSize = 64 * 1024,
                MaxStreamWindowSize = 1024 * 1024,
                WindowScaleThresholdMultiplier = 1.0,
                EnableAdaptiveWindowScaling = true
            }
        };

        var ops = new FakeClientStageOperations();
        var sm = new Http2ClientSessionManager(options, ops, clock);

        // Process inbound DATA to trigger measurement PING.
        var dataFrame = new DataFrame(streamId: 1, data: new byte[100], endStream: false);
        sm.ProcessFrame(dataFrame);

        // Find the emitted measurement PING and advance time.
        var pings = ops.EmittedFrames.OfType<PingFrame>().ToList();
        var measurementPing = pings.First(Http2ClientSessionManager.IsRttPing);

        clock.Advance(TimeSpan.FromMilliseconds(50));

        // Ack the measurement PING.
        var ackFrame = new PingFrame(measurementPing.Data, isAck: true);
        sm.ProcessFrame(ackFrame);

        // Verify MinRtt was recorded.
        var measuredRtt = sm.MinRttForTest;
        Assert.Equal(TimeSpan.FromMilliseconds(50), measuredRtt);
    }

    [Fact(Timeout = 5000)]
    public void Session_should_not_emit_measurement_ping_when_scaling_disabled()
    {
        var clock = new FakeTimeProvider();
        var options = new GaudiClientOptions
        {
            Http2 = new Http2ClientOptions
            {
                EnableAdaptiveWindowScaling = false
            }
        };

        var ops = new FakeClientStageOperations();
        var sm = new Http2ClientSessionManager(options, ops, clock);

        // Process inbound DATA.
        var dataFrame = new DataFrame(streamId: 1, data: new byte[100], endStream: false);
        sm.ProcessFrame(dataFrame);

        // No measurement PINGs should be emitted.
        var measurementPings = ops.EmittedFrames
            .OfType<PingFrame>()
            .Where(Http2ClientSessionManager.IsRttPing)
            .ToList();

        Assert.Empty(measurementPings);
        Assert.Equal(TimeSpan.Zero, sm.MinRttForTest);
    }

    [Fact(Timeout = 5000)]
    public void Session_should_not_send_measurement_ping_when_window_at_max()
    {
        var clock = new FakeTimeProvider();
        var options = new GaudiClientOptions
        {
            Http2 = new Http2ClientOptions
            {
                InitialStreamWindowSize = 64 * 1024,
                MaxStreamWindowSize = 1024 * 1024,
                WindowScaleThresholdMultiplier = 1.0,
                EnableAdaptiveWindowScaling = true
            }
        };

        var ops = new FakeClientStageOperations();
        var sm = new Http2ClientSessionManager(options, ops, clock);

        // Process multiple DATA frames until window grows to max.
        for (int i = 0; i < 20; i++)
        {
            var dataFrame = new DataFrame(streamId: 1, data: new byte[100000], endStream: false);
            sm.ProcessFrame(dataFrame);
            clock.Advance(TimeSpan.FromMilliseconds(150));
        }

        // Clear the frame list to start fresh.
        ops.EmittedFrames.Clear();

        // When the window is at max, no measurement PINGs should be emitted.
        var dataFrame2 = new DataFrame(streamId: 1, data: new byte[100], endStream: false);
        sm.ProcessFrame(dataFrame2);

        var measurementPings = ops.EmittedFrames
            .OfType<PingFrame>()
            .Where(Http2ClientSessionManager.IsRttPing)
            .ToList();

        Assert.Empty(measurementPings);
    }
}
