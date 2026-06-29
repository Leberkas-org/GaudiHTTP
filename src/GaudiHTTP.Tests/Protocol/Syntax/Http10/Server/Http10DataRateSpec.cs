using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Time.Testing;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Body;
using GaudiHTTP.Protocol.Syntax.Http10.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Server.Context.Features;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http10.Server;

public sealed class Http10DataRateSpec
{
    private static IFeatureCollection CreateStreamingResponseContext(long contentLength)
    {
        var features = new GaudiFeatureCollection();
        features.Set<IHttpRequestFeature>(new GaudiHttpRequestFeature());
        var responseFeature = new GaudiHttpResponseFeature { StatusCode = 200 };
        responseFeature.Headers["Content-Length"] = contentLength.ToString();
        features.Set<IHttpResponseFeature>(responseFeature);
        var bodyFeature = new GaudiHttpResponseBodyFeature();
        bodyFeature.UpgradeToPipe();
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        return features;
    }

    private static TransportBuffer MakeBuffer(string raw)
    {
        var data = Encoding.ASCII.GetBytes(raw);
        var buffer = TransportBuffer.Rent(data.Length);
        data.CopyTo(buffer.FullMemory.Span);
        buffer.Length = data.Length;
        return buffer;
    }

    private static TransportBuffer MakeBuffer(byte[] data)
    {
        var buffer = TransportBuffer.Rent(data.Length);
        data.CopyTo(buffer.FullMemory.Span);
        buffer.Length = data.Length;
        return buffer;
    }

    private static Http1ConnectionOptions CreateOptionsWithResponseRate(double minRate, TimeSpan grace)
    {
        var defaultOptions = new GaudiServerOptions().ToHttp1Options();
        var newLimits = defaultOptions.Limits with
        {
            MinResponseDataRate = minRate,
            MinResponseDataRateGracePeriod = grace
        };
        return defaultOptions with { Limits = newLimits };
    }

    private static Http1ConnectionOptions CreateOptionsWithRequestRate(double minRate, TimeSpan grace)
    {
        var defaultOptions = new GaudiServerOptions().ToHttp1Options();
        var newLimits = defaultOptions.Limits with
        {
            MinRequestBodyDataRate = minRate,
            MinRequestBodyDataRateGracePeriod = grace
        };
        return defaultOptions with { Limits = newLimits };
    }

    [Fact(Timeout = 5000)]
    public void Data_rate_monitoring_disabled_by_default()
    {
        var defaultOptions = new GaudiServerOptions().ToHttp1Options();
        var ops = new FakeServerOps();
        var sm = new Http10ServerStateMachine(defaultOptions, ops);

        var requestData = "GET / HTTP/1.0\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        var headerBuffer = MakeBuffer(requestData);
        sm.DecodeClientData(TransportData.Rent(headerBuffer));

        // Fire timer with monitoring disabled — should not schedule another timer
        sm.OnTimerFired("data-rate-check");

        Assert.False(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    public void Fast_response_body_should_not_violate()
    {
        var options = CreateOptionsWithResponseRate(100, TimeSpan.FromSeconds(1));
        var ops = new FakeServerOps();
        var sm = new Http10ServerStateMachine(options, ops);

        var requestData = "GET / HTTP/1.0\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        var headerBuffer = MakeBuffer(requestData);
        sm.DecodeClientData(TransportData.Rent(headerBuffer));

        // Simulate drain complete (body fully sent)
        sm.OnBodyMessage(new BodyReadComplete<int>(0, 0));

        sm.OnTimerFired("data-rate-check");

        Assert.False(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    public void Idle_connection_should_not_be_flagged()
    {
        var options = CreateOptionsWithResponseRate(10000, TimeSpan.FromSeconds(1));
        var ops = new FakeServerOps();
        var sm = new Http10ServerStateMachine(options, ops);

        var requestData = "GET / HTTP/1.0\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        var headerBuffer = MakeBuffer(requestData);
        sm.DecodeClientData(TransportData.Rent(headerBuffer));

        sm.OnTimerFired("data-rate-check");

        Assert.False(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    public void Response_body_rate_within_grace_period_should_not_violate()
    {
        var options = CreateOptionsWithResponseRate(1000, TimeSpan.FromSeconds(5));
        var ops = new FakeServerOps();
        var sm = new Http10ServerStateMachine(options, ops);

        var requestData = "GET / HTTP/1.0\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        var headerBuffer = MakeBuffer(requestData);
        sm.DecodeClientData(TransportData.Rent(headerBuffer));

        sm.OnTimerFired("data-rate-check");

        Assert.False(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    public void Response_completion_should_remove_rate_tracking()
    {
        var options = CreateOptionsWithResponseRate(10000, TimeSpan.FromMilliseconds(100));
        var ops = new FakeServerOps();
        var sm = new Http10ServerStateMachine(options, ops);

        var requestData = "GET / HTTP/1.0\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        var headerBuffer = MakeBuffer(requestData);
        sm.DecodeClientData(TransportData.Rent(headerBuffer));

        Thread.Sleep(150);

        sm.OnTimerFired("data-rate-check");

        Assert.False(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    public void Completed_streaming_response_should_not_flag_idle_connection()
    {
        // Regression: streaming completion (bytesRead == 0) left the response-rate entry in
        // place; the stale entry decayed to 0 B/s and the next data-rate-check after the
        // grace period flagged the healthy idle connection as a violation.
        var options = CreateOptionsWithResponseRate(1000, TimeSpan.FromSeconds(1));
        var clock = new FakeTimeProvider();
        var ops = new FakeServerOps();
        var sm = new Http10ServerStateMachine(options, ops, clock);

        var requestData = "GET / HTTP/1.0\r\nHost: localhost\r\nConnection: keep-alive\r\nContent-Length: 0\r\n\r\n";
        sm.DecodeClientData(TransportData.Rent(MakeBuffer(requestData)));

        var context = CreateStreamingResponseContext(1024);
        sm.OnResponse(context);

        // Simulate body drain via SerialBodyPump: 10 bytes read, then endStream
        sm.OnBodyMessage(new BodyReadComplete<int>(0, 10));
        sm.OnBodyMessage(new BodyReadComplete<int>(0, 0));

        clock.Advance(TimeSpan.FromMilliseconds(600));
        sm.OnTimerFired("data-rate-check");
        clock.Advance(TimeSpan.FromSeconds(10));
        sm.OnTimerFired("data-rate-check");

        Assert.False(sm.ShouldComplete,
            "Idle connection was flagged as a data-rate violation after the response completed.");
    }

    [Fact(Timeout = 5000)]
    public void Slow_response_body_violation_sets_should_complete_with_injected_clock()
    {
        var options = CreateOptionsWithResponseRate(1000, TimeSpan.FromSeconds(1));
        var clock = new FakeTimeProvider();
        var ops = new FakeServerOps();
        var sm = new Http10ServerStateMachine(options, ops, clock);

        var requestData = "GET / HTTP/1.0\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        var headerBuffer = MakeBuffer(requestData);
        sm.DecodeClientData(TransportData.Rent(headerBuffer));

        var context = CreateStreamingResponseContext(1024);
        sm.OnResponse(context);

        // Feed tiny amount of response body via BodyReadComplete
        sm.OnBodyMessage(new BodyReadComplete<int>(0, 10));

        // Advance clock to first check point (600ms, triggers first rate calculation but still in grace)
        clock.Advance(TimeSpan.FromMilliseconds(600));
        sm.OnTimerFired("data-rate-check");
        Assert.False(sm.ShouldComplete, "Should be in grace period at first check");

        // Advance clock past grace period (1700ms total, and grace started at 600ms)
        // Now > GracePeriodStart (600) + 1000ms grace = 1600ms, so should violate
        clock.Advance(TimeSpan.FromMilliseconds(1100));
        sm.OnTimerFired("data-rate-check");
        Assert.True(sm.ShouldComplete, "Expected data rate violation to set ShouldComplete after grace expires");
    }

    [Fact(Timeout = 5000)]
    public void Fast_response_body_within_grace_should_not_violate_with_injected_clock()
    {
        var options = CreateOptionsWithResponseRate(1000, TimeSpan.FromSeconds(5));
        var clock = new FakeTimeProvider();
        var ops = new FakeServerOps();
        var sm = new Http10ServerStateMachine(options, ops, clock);

        var requestData = "GET / HTTP/1.0\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        var headerBuffer = MakeBuffer(requestData);
        sm.DecodeClientData(TransportData.Rent(headerBuffer));

        // Check at time=600ms (first rate check, enters grace)
        clock.Advance(TimeSpan.FromMilliseconds(600));
        sm.OnTimerFired("data-rate-check");
        Assert.False(sm.ShouldComplete);

        // Check at time=3600ms (within 5s grace period from 600ms = 5600ms) — should still be OK
        clock.Advance(TimeSpan.FromMilliseconds(3000));
        sm.OnTimerFired("data-rate-check");
        Assert.False(sm.ShouldComplete, "Should not abort when within grace period");
    }

    [Fact(Timeout = 5000)]
    public void Slow_request_body_violation_sets_should_complete_with_injected_clock()
    {
        var options = CreateOptionsWithRequestRate(1000, TimeSpan.FromSeconds(1));
        var clock = new FakeTimeProvider();
        var ops = new FakeServerOps();
        var sm = new Http10ServerStateMachine(options, ops, clock);

        // Send request headers + indicate body will come
        var requestData = "POST / HTTP/1.0\r\nHost: localhost\r\nContent-Length: 10\r\n\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(requestData);
        var buffer = MakeBuffer(headerBytes);
        sm.DecodeClientData(TransportData.Rent(buffer));

        // At time=0, send first chunk of body (5 bytes)
        var bodyChunk1 = new byte[5];
        var buffer2 = MakeBuffer(bodyChunk1);
        sm.DecodeClientData(TransportData.Rent(buffer2));

        // Advance clock to first check point (600ms)
        clock.Advance(TimeSpan.FromMilliseconds(600));
        sm.OnTimerFired("data-rate-check");
        Assert.False(sm.ShouldComplete, "Should be in grace period at first check");

        // Advance clock past grace period (1700ms total)
        // Only 5 bytes sent in 1700ms = 2.94 bytes/sec << 1000, so violation
        clock.Advance(TimeSpan.FromMilliseconds(1100));
        sm.OnTimerFired("data-rate-check");
        Assert.True(sm.ShouldComplete, "Expected request body data rate violation after grace expires");
    }
}
