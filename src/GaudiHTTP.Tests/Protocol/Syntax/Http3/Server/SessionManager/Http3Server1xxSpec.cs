using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Syntax.Http3.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Server.Context.Features;
using GaudiHTTP.Tests.Shared;
using GaudiHTTP.Tests.TestSupport;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http3.Server.SessionManager;

public sealed class Http3Server1xxSpec
{
    private static Http3ConnectionOptions DefaultOptions() => ServerOptionDefaults.Http3();

    private static void SendRequest(Http3ServerSessionManager sm, long streamId)
    {
        var data = ServerOptionDefaults.BuildHttp3Request("GET", "/");
        sm.DecodeClientData(new ServerStreamAccepted(StreamTarget.FromId(streamId),
            StreamDirection.Bidirectional));
        var buffer = WireBuffer.Rent(data.Length);
        data.CopyTo(buffer.FullMemory.Span);
        buffer.Length = data.Length;
        sm.DecodeClientData(MultiplexedData.Rent(buffer, streamId));
        sm.DecodeClientData(new StreamReadCompleted(StreamTarget.FromId(streamId)));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Feature_should_be_registered_on_request_dispatch()
    {
        var ops = new FakeServerOps();
        var sm = new Http3ServerSessionManager(DefaultOptions(), ops);
        SendRequest(sm, 0);

        Assert.Single(ops.Requests);
        var feature = ops.Requests[0].Get<GaudiInformationalResponseFeature>();
        Assert.NotNull(feature);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void SendInformational_should_not_close_stream()
    {
        var ops = new FakeServerOps();
        var sm = new Http3ServerSessionManager(DefaultOptions(), ops);
        SendRequest(sm, 0);

        var features = ops.Requests[0];
        features.Get<GaudiInformationalResponseFeature>()!
            .SendInformational(100, new HeaderDictionary());

        var responseFeature = features.Get<IHttpResponseFeature>()!;
        responseFeature.StatusCode = 200;
        var ex = Record.Exception(() => sm.OnResponse(features));

        Assert.Null(ex);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void SendInformational_should_emit_outbound_data()
    {
        var ops = new FakeServerOps();
        var sm = new Http3ServerSessionManager(DefaultOptions(), ops);
        SendRequest(sm, 0);

        var features = ops.Requests[0];
        var outboundBefore = ops.Outbound.Count;

        features.Get<GaudiInformationalResponseFeature>()!
            .SendInformational(103, new HeaderDictionary { ["Link"] = "</style.css>; rel=preload" });

        Assert.True(ops.Outbound.Count > outboundBefore);
    }
}
