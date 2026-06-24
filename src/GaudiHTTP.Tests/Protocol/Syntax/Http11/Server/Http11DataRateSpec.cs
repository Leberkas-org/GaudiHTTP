using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Time.Testing;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Body;
using GaudiHTTP.Protocol.Syntax.Http11.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Server.Context.Features;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http11.Server;

public sealed class Http11DataRateSpec
{
    private static TurboFeatureCollection CreateResponseContext()
    {
        var features = new TurboFeatureCollection();
        features.Set<IHttpRequestFeature>(new GaudiHttpRequestFeature());
        features.Set<IHttpResponseFeature>(new GaudiHttpResponseFeature { StatusCode = 200 });
        var bodyFeature = new GaudiHttpResponseBodyFeature();
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
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

    private static Http1ConnectionOptions CreateOptionsWithResponseRate(double minRate, TimeSpan grace)
    {
        var defaultOptions = new TurboServerOptions().ToHttp1Options();
        var newLimits = defaultOptions.Limits with
        {
            MinResponseDataRate = minRate,
            MinResponseDataRateGracePeriod = grace
        };
        return defaultOptions with { Limits = newLimits };
    }

    private static Http1ConnectionOptions CreateOptionsWithRequestRate(double minRate, TimeSpan grace)
    {
        var defaultOptions = new TurboServerOptions().ToHttp1Options();
        var newLimits = defaultOptions.Limits with
        {
            MinRequestBodyDataRate = minRate,
            MinRequestBodyDataRateGracePeriod = grace
        };
        return defaultOptions with { Limits = newLimits };
    }

    [Fact(Timeout = 5000)]
    public void Slow_request_body_violation_sets_should_complete_with_injected_clock()
    {
        var options = CreateOptionsWithRequestRate(1000, TimeSpan.FromSeconds(1));
        var clock = new FakeTimeProvider();
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(options, new TurboServerOptions().ToHttp2Options(), ops, clock);

        // Chunked request body forces streaming (small Content-Length bodies are buffered, not observed).
        // One small chunk arrives, then the upload stalls without the terminating chunk.
        var headersAndPartialChunk = "POST / HTTP/1.1\r\nHost: localhost\r\nTransfer-Encoding: chunked\r\n\r\n5\r\nAAAAA\r\n";
        sm.DecodeClientData(TransportData.Rent(MakeBuffer(headersAndPartialChunk)));

        clock.Advance(TimeSpan.FromMilliseconds(600));
        sm.OnTimerFired("data-rate-check");
        Assert.False(sm.ShouldComplete, "Should be in grace period at first check");

        // 5 bytes in 1700ms = ~2.9 bytes/sec << 1000, grace (1s) expired → violation.
        clock.Advance(TimeSpan.FromMilliseconds(1100));
        sm.OnTimerFired("data-rate-check");
        Assert.True(sm.ShouldComplete, "Expected request body data rate violation after grace expires");
    }

    [Fact(Timeout = 5000)]
    public void Data_rate_monitoring_disabled_by_default()
    {
        var defaultOptions = new TurboServerOptions().ToHttp1Options();
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(defaultOptions, new TurboServerOptions().ToHttp2Options(), ops);

        const string requestData = "GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        var headerBuffer = MakeBuffer(requestData);
        sm.DecodeClientData(TransportData.Rent(headerBuffer));

        var context = CreateResponseContext();
        sm.OnResponse(context);

        sm.OnBodyMessage(new DrainReadComplete<int>(0,0));

        // Fire timer with monitoring disabled — should not schedule another timer
        sm.OnTimerFired("data-rate-check");

        Assert.False(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    public void Fast_response_body_should_not_violate()
    {
        var options = CreateOptionsWithResponseRate(100, TimeSpan.FromSeconds(1));
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(options, new TurboServerOptions().ToHttp2Options(), ops);

        const string requestData = "GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        var headerBuffer = MakeBuffer(requestData);
        sm.DecodeClientData(TransportData.Rent(headerBuffer));

        var context = CreateResponseContext();
        sm.OnResponse(context);

        // Send large response body quickly (exceeds minimum rate)
        sm.OnBodyMessage(new DrainReadComplete<int>(0,5000));

        sm.OnTimerFired("data-rate-check");

        Assert.False(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    public void Idle_connection_should_not_be_flagged()
    {
        var options = CreateOptionsWithResponseRate(10000, TimeSpan.FromSeconds(1));
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(options, new TurboServerOptions().ToHttp2Options(), ops);

        const string requestData = "GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        var headerBuffer = MakeBuffer(requestData);
        sm.DecodeClientData(TransportData.Rent(headerBuffer));

        var context = CreateResponseContext();
        sm.OnResponse(context);

        sm.OnBodyMessage(new DrainReadComplete<int>(0,0));

        sm.OnTimerFired("data-rate-check");

        Assert.False(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    public void Response_body_rate_within_grace_period_should_not_violate()
    {
        var options = CreateOptionsWithResponseRate(1000, TimeSpan.FromSeconds(5));
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(options, new TurboServerOptions().ToHttp2Options(), ops);

        const string requestData = "GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        var headerBuffer = MakeBuffer(requestData);
        sm.DecodeClientData(TransportData.Rent(headerBuffer));

        var context = CreateResponseContext();
        sm.OnResponse(context);

        sm.OnBodyMessage(new DrainReadComplete<int>(0,10));

        sm.OnTimerFired("data-rate-check");

        Assert.False(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    public async Task Response_completion_should_remove_rate_tracking()
    {
        var options = CreateOptionsWithResponseRate(10000, TimeSpan.FromMilliseconds(100));
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(options, new TurboServerOptions().ToHttp2Options(), ops);

        const string requestData = "GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        var headerBuffer = MakeBuffer(requestData);
        sm.DecodeClientData(TransportData.Rent(headerBuffer));

        var context = CreateResponseContext();
        sm.OnResponse(context);

        sm.OnBodyMessage(new DrainReadComplete<int>(0,1));

        sm.OnBodyMessage(new DrainReadComplete<int>(0,0));

        await Task.Delay(150, TestContext.Current.CancellationToken);

        sm.OnTimerFired("data-rate-check");

        Assert.False(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    public void Rate_timer_should_be_scheduled_once_until_it_fires()
    {
        // EnsureRateTimer used to re-schedule the Akka timer on every observed chunk —
        // one redundant scheduler call per response chunk on the hot path.
        var options = CreateOptionsWithResponseRate(240, TimeSpan.FromSeconds(5));
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(options, new TurboServerOptions().ToHttp2Options(), ops);

        const string requestData = "GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        sm.DecodeClientData(TransportData.Rent(MakeBuffer(requestData)));

        var context = CreateResponseContext();
        sm.OnResponse(context);

        // Stream several response chunks; each one observes bytes in the rate monitor.
        for (var i = 0; i < 5; i++)
        {
            sm.OnBodyMessage(new DrainReadComplete<int>(0,1024));
        }

        Assert.Equal(1, ops.ScheduleTimerCalls.Count(t => t.Name == "data-rate-check"));

        // Once the timer fires it may be re-armed (entries still active) — exactly once.
        sm.OnTimerFired("data-rate-check");
        sm.OnBodyMessage(new DrainReadComplete<int>(0,1024));
        sm.OnBodyMessage(new DrainReadComplete<int>(0,1024));

        Assert.Equal(2, ops.ScheduleTimerCalls.Count(t => t.Name == "data-rate-check"));
    }

    [Fact(Timeout = 5000)]
    public void Buffered_response_completion_should_not_flag_idle_keepalive_connection()
    {
        // Regression: EmitBufferedBody observed response bytes in the rate monitor but never
        // removed the entry on completion. The stale entry decayed to 0 B/s and the next
        // data-rate-check after the grace period killed the healthy idle keep-alive connection
        // (benchmark: HttpClient got "connection forcibly closed" on concurrent H1.1 uploads).
        var options = CreateOptionsWithResponseRate(240, TimeSpan.FromSeconds(1));
        var clock = new FakeTimeProvider();
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(options, new TurboServerOptions().ToHttp2Options(), ops, clock);

        const string requestData = "GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        sm.DecodeClientData(TransportData.Rent(MakeBuffer(requestData)));

        // Buffered response body: written into the feature before OnResponse, emitted
        // synchronously via EmitBufferedBody (the standard path for normal responses).
        var context = CreateResponseContext();
        var bodyFeature = (GaudiHttpResponseBodyFeature)context.Get<IHttpResponseBodyFeature>()!;
        var span = bodyFeature.Writer.GetSpan(64);
        span[..64].Fill(0x41);
        bodyFeature.Writer.Advance(64);
        bodyFeature.Writer.Complete();
        sm.OnResponse(context);

        // Connection sits idle on keep-alive well past the grace period; the periodic
        // check fires repeatedly (first below-rate check starts the grace window).
        clock.Advance(TimeSpan.FromMilliseconds(600));
        sm.OnTimerFired("data-rate-check");
        clock.Advance(TimeSpan.FromSeconds(10));
        sm.OnTimerFired("data-rate-check");

        Assert.False(sm.ShouldComplete,
            "Idle keep-alive connection was flagged as a data-rate violation after a buffered response completed.");
    }

    [Fact(Timeout = 5000)]
    public void Slow_response_body_violation_sets_should_complete_with_injected_clock()
    {
        var options = CreateOptionsWithResponseRate(1000, TimeSpan.FromSeconds(1));
        var clock = new FakeTimeProvider();
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(options, new TurboServerOptions().ToHttp2Options(), ops, clock);

        const string requestData = "GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        var headerBuffer = MakeBuffer(requestData);
        sm.DecodeClientData(TransportData.Rent(headerBuffer));

        var context = CreateResponseContext();
        sm.OnResponse(context);

        // Feed tiny amount of response body (will be observed at time=0)
        sm.OnBodyMessage(new DrainReadComplete<int>(0,10));

        // Advance clock to first check point (600ms, triggers first rate calculation but still in grace)
        // With 10 bytes in 600ms = 16.67 bytes/sec < 1000 bytes/sec, enters grace period
        clock.Advance(TimeSpan.FromMilliseconds(600));
        sm.OnTimerFired("data-rate-check");
        Assert.False(sm.ShouldComplete, "Should be in grace period at first check");

        // Advance clock past grace period (1100ms total, and grace started at 600ms)
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
        var sm = new Http11ServerStateMachine(options, new TurboServerOptions().ToHttp2Options(), ops, clock);

        const string requestData = "GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        var headerBuffer = MakeBuffer(requestData);
        sm.DecodeClientData(TransportData.Rent(headerBuffer));

        var context = CreateResponseContext();
        sm.OnResponse(context);

        // Feed tiny amount at time=0
        sm.OnBodyMessage(new DrainReadComplete<int>(0,10));

        // Check at time=600ms (first rate check, enters grace)
        clock.Advance(TimeSpan.FromMilliseconds(600));
        sm.OnTimerFired("data-rate-check");
        Assert.False(sm.ShouldComplete);

        // Check at time=3600ms (within 5s grace period from 600ms = 5600ms) — should still be OK
        clock.Advance(TimeSpan.FromMilliseconds(3000));
        sm.OnTimerFired("data-rate-check");
        Assert.False(sm.ShouldComplete, "Should not abort when within grace period");
    }
}