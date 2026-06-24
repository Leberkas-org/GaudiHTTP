using GaudiHTTP.Client;
using GaudiHTTP.Protocol.Syntax.Http3.Client;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http3.Client;

public sealed class Http3EncodeRequestStreamLimitSpec
{
    private static Http3ClientSessionManager CreateSessionManager(FakeClientOps ops, int maxConcurrentStreams)
    {
        var clientOpts = new GaudiClientOptions();
        clientOpts.Http3.MaxConcurrentStreams = maxConcurrentStreams;
        return new Http3ClientSessionManager(
            clientOpts.ToHttp3EncoderOptions(),
            clientOpts.ToHttp3DecoderOptions(),
            clientOpts,
            ops);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.1")]
    public void CanOpenStream_should_return_false_when_MaxConcurrentStreams_reached()
    {
        var ops = new FakeClientOps();
        var mgr = CreateSessionManager(ops, maxConcurrentStreams: 1);

        var request1 = new HttpRequestMessage(HttpMethod.Get, "https://localhost/a");
        mgr.EncodeRequest(request1);

        Assert.False(mgr.CanOpenStream);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.1")]
    public void CanOpenStream_should_return_true_when_below_limit()
    {
        var ops = new FakeClientOps();
        var mgr = CreateSessionManager(ops, maxConcurrentStreams: 2);

        var request1 = new HttpRequestMessage(HttpMethod.Get, "https://localhost/a");
        mgr.EncodeRequest(request1);

        Assert.True(mgr.CanOpenStream);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.1")]
    public void EncodeRequest_should_not_throw_when_limit_reached()
    {
        var ops = new FakeClientOps();
        var mgr = CreateSessionManager(ops, maxConcurrentStreams: 1);

        mgr.EncodeRequest(new HttpRequestMessage(HttpMethod.Get, "https://localhost/a"));

        var ex = Record.Exception(() =>
            mgr.EncodeRequest(new HttpRequestMessage(HttpMethod.Get, "https://localhost/b")));

        Assert.Null(ex);
    }
}
