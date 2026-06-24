using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Syntax.Http3;
using GaudiHTTP.Protocol.Syntax.Http3.Qpack;
using GaudiHTTP.Protocol.Syntax.Http3.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Server.Context.Features;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http3.Server.SessionManager;

public sealed class Http3Server1xxSpec
{
    private static Http3ConnectionOptions DefaultOptions() => new()
    {
        Limits = new ResolvedServerLimits(
            MaxRequestBodySize: 30 * 1024 * 1024,
            KeepAliveTimeout: TimeSpan.FromSeconds(130),
            RequestHeadersTimeout: TimeSpan.FromSeconds(30),
            MinRequestBodyDataRate: 240,
            MinRequestBodyDataRateGracePeriod: TimeSpan.FromSeconds(5),
            MinResponseDataRate: 240,
            MinResponseDataRateGracePeriod: TimeSpan.FromSeconds(5)),
        MaxConcurrentStreams = 100,
        MaxHeaderListSize = 32 * 1024,
        MaxHeaderCount = 100,
        QpackMaxTableCapacity = 0,
        QpackBlockedStreams = 0,
        MaxResponseBufferSize = 64 * 1024,
        ResponseBodyChunkSize = 16 * 1024,
        BodyConsumptionTimeout = TimeSpan.FromSeconds(30),
        UseHuffman = true,
    };

    private static byte[] BuildRequest(string method, string path)
    {
        var tableSync = new QpackTableSync(0, 0, 0, 0);
        var headers = new List<(string, string)>
        {
            (":method", method),
            (":path", path),
            (":scheme", "https"),
            (":authority", "localhost"),
        };
        var headerBlock = tableSync.Encoder.Encode(headers);
        var frame = new HeadersFrame(headerBlock);
        var buf = new byte[frame.SerializedSize];
        var span = buf.AsSpan();
        frame.WriteTo(ref span);
        return buf;
    }

    private static void SendRequest(Http3ServerSessionManager sm, long streamId)
    {
        var data = BuildRequest("GET", "/");
        sm.DecodeClientData(new ServerStreamAccepted(StreamTarget.FromId(streamId),
            StreamDirection.Bidirectional));
        var buffer = TransportBuffer.Rent(data.Length);
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
