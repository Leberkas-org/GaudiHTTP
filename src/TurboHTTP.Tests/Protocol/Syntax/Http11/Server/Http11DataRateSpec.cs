using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.Syntax.Http11.Server;
using TurboHTTP.Server;
using TurboHTTP.Server.Context.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http11.Server;

public sealed class Http11DataRateSpec
{
    private static IFeatureCollection CreateResponseContext()
    {
        var features = new TurboFeatureCollection();
        features.Set<IHttpRequestFeature>(new TurboHttpRequestFeature());
        features.Set<IHttpResponseFeature>(new TurboHttpResponseFeature { StatusCode = 200 });
        var bodyFeature = new TurboHttpResponseBodyFeature();
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

    private static TransportBuffer MakeBuffer(byte[] data)
    {
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
    public void Data_rate_monitoring_disabled_by_default()
    {
        var defaultOptions = new TurboServerOptions().ToHttp1Options();
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(defaultOptions, new TurboServerOptions().ToHttp2Options(), ops);

        var requestData = "GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        var headerBuffer = MakeBuffer(requestData);
        sm.DecodeClientData(new TransportData(headerBuffer));

        var context = CreateResponseContext();
        sm.OnResponse(context);

        sm.OnBodyMessage(new OutboundBodyComplete());

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

        var requestData = "GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        var headerBuffer = MakeBuffer(requestData);
        sm.DecodeClientData(new TransportData(headerBuffer));

        var context = CreateResponseContext();
        sm.OnResponse(context);

        // Send large response body quickly (exceeds minimum rate)
        var largeBody = new byte[5000];
        var owner = System.Buffers.MemoryPool<byte>.Shared.Rent(largeBody.Length);
        largeBody.CopyTo(owner.Memory.Span);
        sm.OnBodyMessage(new OutboundBodyChunk(owner, largeBody.Length));

        sm.OnTimerFired("data-rate-check");

        Assert.False(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    public void Idle_connection_should_not_be_flagged()
    {
        var options = CreateOptionsWithResponseRate(10000, TimeSpan.FromSeconds(1));
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(options, new TurboServerOptions().ToHttp2Options(), ops);

        var requestData = "GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        var headerBuffer = MakeBuffer(requestData);
        sm.DecodeClientData(new TransportData(headerBuffer));

        var context = CreateResponseContext();
        sm.OnResponse(context);

        sm.OnBodyMessage(new OutboundBodyComplete());

        sm.OnTimerFired("data-rate-check");

        Assert.False(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    public void Response_body_rate_within_grace_period_should_not_violate()
    {
        var options = CreateOptionsWithResponseRate(1000, TimeSpan.FromSeconds(5));
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(options, new TurboServerOptions().ToHttp2Options(), ops);

        var requestData = "GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        var headerBuffer = MakeBuffer(requestData);
        sm.DecodeClientData(new TransportData(headerBuffer));

        var context = CreateResponseContext();
        sm.OnResponse(context);

        var responseBody = new byte[10];
        var owner = System.Buffers.MemoryPool<byte>.Shared.Rent(responseBody.Length);
        responseBody.CopyTo(owner.Memory.Span);
        sm.OnBodyMessage(new OutboundBodyChunk(owner, responseBody.Length));

        sm.OnTimerFired("data-rate-check");

        Assert.False(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    public void Response_completion_should_remove_rate_tracking()
    {
        var options = CreateOptionsWithResponseRate(10000, TimeSpan.FromMilliseconds(100));
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(options, new TurboServerOptions().ToHttp2Options(), ops);

        var requestData = "GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        var headerBuffer = MakeBuffer(requestData);
        sm.DecodeClientData(new TransportData(headerBuffer));

        var context = CreateResponseContext();
        sm.OnResponse(context);

        var responseBody = new byte[1];
        var owner = System.Buffers.MemoryPool<byte>.Shared.Rent(responseBody.Length);
        responseBody.CopyTo(owner.Memory.Span);
        sm.OnBodyMessage(new OutboundBodyChunk(owner, responseBody.Length));

        sm.OnBodyMessage(new OutboundBodyComplete());

        System.Threading.Thread.Sleep(150);

        sm.OnTimerFired("data-rate-check");

        Assert.False(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    public void Slow_response_body_violation_sets_should_complete_with_injected_clock()
    {
        var options = CreateOptionsWithResponseRate(1000, TimeSpan.FromSeconds(1));
        long now = 0;
        Func<long> clock = () => now;
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(options, new TurboServerOptions().ToHttp2Options(), ops, clock);

        var requestData = "GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        var headerBuffer = MakeBuffer(requestData);
        sm.DecodeClientData(new TransportData(headerBuffer));

        var context = CreateResponseContext();
        sm.OnResponse(context);

        // Feed tiny amount of response body (will be observed at time=0)
        var responseBody = new byte[10];
        var owner = System.Buffers.MemoryPool<byte>.Shared.Rent(responseBody.Length);
        responseBody.CopyTo(owner.Memory.Span);
        sm.OnBodyMessage(new OutboundBodyChunk(owner, responseBody.Length));

        // Advance clock to first check point (600ms, triggers first rate calculation but still in grace)
        // With 10 bytes in 600ms = 16.67 bytes/sec < 1000 bytes/sec, enters grace period
        now = 600;
        sm.OnTimerFired("data-rate-check");
        Assert.False(sm.ShouldComplete, "Should be in grace period at first check");

        // Advance clock past grace period (1100ms total, and grace started at 600ms)
        // Now > GracePeriodStart (600) + 1000ms grace = 1600ms, so should violate
        now = 1700;
        sm.OnTimerFired("data-rate-check");
        Assert.True(sm.ShouldComplete, "Expected data rate violation to set ShouldComplete after grace expires");
    }

    [Fact(Timeout = 5000)]
    public void Fast_response_body_within_grace_should_not_violate_with_injected_clock()
    {
        var options = CreateOptionsWithResponseRate(1000, TimeSpan.FromSeconds(5));
        long now = 0;
        Func<long> clock = () => now;
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(options, new TurboServerOptions().ToHttp2Options(), ops, clock);

        var requestData = "GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        var headerBuffer = MakeBuffer(requestData);
        sm.DecodeClientData(new TransportData(headerBuffer));

        var context = CreateResponseContext();
        sm.OnResponse(context);

        // Feed tiny amount at time=0
        var responseBody = new byte[10];
        var owner = System.Buffers.MemoryPool<byte>.Shared.Rent(responseBody.Length);
        responseBody.CopyTo(owner.Memory.Span);
        sm.OnBodyMessage(new OutboundBodyChunk(owner, responseBody.Length));

        // Check at time=600ms (first rate check, enters grace)
        now = 600;
        sm.OnTimerFired("data-rate-check");
        Assert.False(sm.ShouldComplete);

        // Check at time=3600ms (within 5s grace period from 600ms = 5600ms) — should still be OK
        now = 3600;
        sm.OnTimerFired("data-rate-check");
        Assert.False(sm.ShouldComplete, "Should not abort when within grace period");
    }
}
