using GaudiHTTP.Client;
using GaudiHTTP.Protocol.Syntax.Http2.Client;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Client;

public sealed class Http2EncodeRequestStreamLimitSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1.2")]
    public void CanOpenStream_should_return_false_when_MaxConcurrentStreams_reached()
    {
        var opts = new GaudiClientOptions();
        opts.Http2.MaxConcurrentStreams = 1;
        var ops = new FakeClientOps();
        var mgr = new Http2ClientSessionManager(opts, ops);

        mgr.EncodeRequest(new HttpRequestMessage(HttpMethod.Get, "https://localhost/a"));

        Assert.False(mgr.CanOpenStream);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1.2")]
    public void CanOpenStream_should_return_true_when_below_limit()
    {
        var opts = new GaudiClientOptions();
        opts.Http2.MaxConcurrentStreams = 2;
        var ops = new FakeClientOps();
        var mgr = new Http2ClientSessionManager(opts, ops);

        mgr.EncodeRequest(new HttpRequestMessage(HttpMethod.Get, "https://localhost/a"));

        Assert.True(mgr.CanOpenStream);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1.2")]
    public void EncodeRequest_should_not_throw_when_limit_reached()
    {
        var opts = new GaudiClientOptions();
        opts.Http2.MaxConcurrentStreams = 1;
        var ops = new FakeClientOps();
        var mgr = new Http2ClientSessionManager(opts, ops);

        mgr.EncodeRequest(new HttpRequestMessage(HttpMethod.Get, "https://localhost/a"));

        var ex = Record.Exception(() =>
            mgr.EncodeRequest(new HttpRequestMessage(HttpMethod.Get, "https://localhost/b")));

        Assert.Null(ex);
    }
}
