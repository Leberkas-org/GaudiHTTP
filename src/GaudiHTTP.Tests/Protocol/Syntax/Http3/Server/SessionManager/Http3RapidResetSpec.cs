using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Syntax.Http3.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http3.Server.SessionManager;

public sealed class Http3RapidResetSpec
{
    private static Http3ConnectionOptions OptionsWithResetBudget(int budget) => new()
    {
        Limits = new ResolvedServerLimits(
            MaxRequestBodySize: 30 * 1024 * 1024,
            KeepAliveTimeout: TimeSpan.FromSeconds(130),
            RequestHeadersTimeout: TimeSpan.FromSeconds(30),
            MinRequestBodyDataRate: 240,
            MinRequestBodyDataRateGracePeriod: TimeSpan.FromSeconds(5),
            MinResponseDataRate: 240,
            MinResponseDataRateGracePeriod: TimeSpan.FromSeconds(5),
            MaxResetStreamsPerWindow: budget,
            RapidResetDetectionWindow: TimeSpan.FromSeconds(30)),
        MaxConcurrentStreams = 100,
        MaxHeaderListSize = 32 * 1024,
        MaxHeaderCount = 100,
        QpackMaxTableCapacity = 0,
        QpackBlockedStreams = 0,
        BodyConsumptionTimeout = TimeSpan.FromSeconds(30),
        UseHuffman = true,
    };

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-8.1")]
    public void Excessive_stream_resets_should_request_connection_completion()
    {
        // CVE-2023-44487 (Rapid Reset), HTTP/3 variant: a client that opens-and-aborts request streams
        // faster than the budget must be cut off; MaxConcurrentStreams never saturates under this attack.
        // A QUIC RESET_STREAM surfaces as StreamClosed(id, DisconnectReason.Error).
        var ops = new FakeServerOps();
        var sm = new Http3ServerSessionManager(OptionsWithResetBudget(5), ops);

        for (var i = 0; i < 6; i++)
        {
            var streamId = i * 4L; // client-initiated bidirectional stream IDs
            sm.DecodeClientData(new StreamClosed(StreamTarget.FromId(streamId), DisconnectReason.Error));
        }

        Assert.True(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-8.1")]
    public void Resets_below_threshold_should_not_terminate_the_connection()
    {
        var ops = new FakeServerOps();
        var sm = new Http3ServerSessionManager(OptionsWithResetBudget(5), ops);

        for (var i = 0; i < 4; i++)
        {
            var streamId = i * 4L;
            sm.DecodeClientData(new StreamClosed(StreamTarget.FromId(streamId), DisconnectReason.Error));
        }

        Assert.False(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-8.1")]
    public void Graceful_stream_completion_should_not_count_as_a_reset()
    {
        // StreamReadCompleted is a normal FIN, not an abort — it must never trip the reset budget.
        var ops = new FakeServerOps();
        var sm = new Http3ServerSessionManager(OptionsWithResetBudget(5), ops);

        for (var i = 0; i < 20; i++)
        {
            var streamId = i * 4L;
            sm.DecodeClientData(new StreamReadCompleted(StreamTarget.FromId(streamId)));
        }

        Assert.False(sm.ShouldComplete);
    }
}
