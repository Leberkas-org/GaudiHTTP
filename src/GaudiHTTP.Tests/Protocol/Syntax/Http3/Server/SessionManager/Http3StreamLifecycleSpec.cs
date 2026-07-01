using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Syntax.Http3;
using GaudiHTTP.Protocol.Syntax.Http3.Qpack;
using GaudiHTTP.Protocol.Syntax.Http3.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Server.Context.Features;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http3.Server.SessionManager;

public sealed class Http3StreamLifecycleSpec
{
    private static Http3ConnectionOptions DefaultConnectionOptions() => new()
    {
        Limits = new ResolvedServerLimits(
            MaxRequestBodySize: 30 * 1024 * 1024,
            KeepAliveTimeout: TimeSpan.FromSeconds(130),
            RequestHeadersTimeout: TimeSpan.FromSeconds(30),
            MinRequestBodyDataRate: 240,
            MinRequestBodyDataRateGracePeriod: TimeSpan.FromSeconds(5),
            MinResponseDataRate: 240,
            MinResponseDataRateGracePeriod: TimeSpan.FromSeconds(5),
            MaxResetStreamsPerWindow: 200,
            RapidResetDetectionWindow: TimeSpan.FromSeconds(30)),
        MaxConcurrentStreams = 100,
        MaxHeaderListSize = 32 * 1024,
        MaxHeaderCount = 100,
        QpackMaxTableCapacity = 0,
        QpackBlockedStreams = 0,
        BodyConsumptionTimeout = TimeSpan.FromSeconds(30),
        UseHuffman = true,
        MaxBufferedBodySize = 64 * 1024,
        ResponseBodyChunkSize = 16 * 1024,
    };

    private static IFeatureCollection CreateResponseContext(long streamId = 999)
    {
        var features = new GaudiFeatureCollection();
        features.Set<IHttpRequestFeature>(new GaudiHttpRequestFeature());
        features.Set<IHttpResponseFeature>(new GaudiHttpResponseFeature { StatusCode = 200 });
        features.Set<IHttpStreamIdFeature>(new GaudiStreamIdFeature(streamId));
        var bodyFeature = new GaudiHttpResponseBodyFeature();
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        return features;
    }


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

    private static void SendRequest(Http3ServerSessionManager sm, long streamId, string method = "GET",
        string path = "/")
    {
        var data = BuildRequest(method, path);
        sm.DecodeClientData(new ServerStreamAccepted(StreamTarget.FromId(streamId),
            StreamDirection.Bidirectional));
        var buffer = TransportBuffer.Rent(data.Length);
        data.CopyTo(buffer.FullMemory.Span);
        buffer.Length = data.Length;
        sm.DecodeClientData(MultiplexedData.Rent(buffer, streamId));
        sm.DecodeClientData(new StreamReadCompleted(StreamTarget.FromId(streamId)));
    }

    private static Http3ServerSessionManager CreateSM(FakeServerOps ops)
    {
        return new Http3ServerSessionManager(DefaultConnectionOptions(), ops);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Request_should_be_emitted_after_StreamReadCompleted()
    {
        var ops = new FakeServerOps();
        var sm = CreateSM(ops);

        const long streamId = 4;
        SendRequest(sm, streamId);

        Assert.Single(ops.Requests);
        var context = ops.Requests[0];

        var streamIdFeature = context.Get<IHttpStreamIdFeature>();
        Assert.NotNull(streamIdFeature);
        Assert.Equal(streamId, streamIdFeature.StreamId);
        var requestFeature = context.Get<IHttpRequestFeature>() as GaudiHttpRequestFeature;
        Assert.NotNull(requestFeature);
        Assert.Equal("GET", requestFeature.Method);
        Assert.Equal("https", requestFeature.Scheme);
        Assert.Equal("localhost", requestFeature.ExtractedHost);
        Assert.Equal("/", requestFeature.Path);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.1")]
    public void Multiple_concurrent_streams_should_all_emit_requests()
    {
        var ops = new FakeServerOps();
        var sm = CreateSM(ops);

        const long streamId1 = 0;
        const long streamId2 = 4;

        SendRequest(sm, streamId1, "GET", "/path1");
        SendRequest(sm, streamId2, "POST", "/path2");

        Assert.Equal(2, ops.Requests.Count);

        var ctx1 = ops.Requests[0];
        var ctx2 = ops.Requests[1];

        var streamIdFeature1 = ctx1.Get<IHttpStreamIdFeature>();
        Assert.NotNull(streamIdFeature1);
        var streamIdFeature2 = ctx2.Get<IHttpStreamIdFeature>();
        Assert.NotNull(streamIdFeature2);
        Assert.Equal(streamId1, streamIdFeature1.StreamId);
        Assert.Equal(streamId2, streamIdFeature2.StreamId);

        var requestFeature1 = ctx1.Get<IHttpRequestFeature>() as GaudiHttpRequestFeature;
        var requestFeature2 = ctx2.Get<IHttpRequestFeature>() as GaudiHttpRequestFeature;
        Assert.NotNull(requestFeature1);
        Assert.NotNull(requestFeature2);
        Assert.Equal("GET", requestFeature1.Method);
        Assert.Equal("POST", requestFeature2.Method);
        Assert.Equal("/path1", requestFeature1.Path);
        Assert.Equal("/path2", requestFeature2.Path);
    }

    [Fact(Timeout = 5000)]
    public void OnResponse_for_unknown_stream_should_not_crash()
    {
        var ops = new FakeServerOps();
        var sm = CreateSM(ops);

        // Should not throw when responding on unknown stream
        var context = CreateResponseContext();
        sm.OnResponse(context);

        // No requests should be emitted (stream 999 never existed)
        Assert.Empty(ops.Requests);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void OnResponse_no_body_should_emit_CompleteWrites()
    {
        var ops = new FakeServerOps();
        var sm = CreateSM(ops);

        const long streamId = 8;
        SendRequest(sm, streamId);

        Assert.Single(ops.Requests);
        var context = ops.Requests[0];

        ops.Outbound.Clear();

        context.Get<IHttpResponseFeature>()?.StatusCode = 200;
        sm.OnResponse(context);

        var completeWrites = ops.Outbound.OfType<CompleteWrites>().ToList();
        Assert.Single(completeWrites);
        Assert.Equal(streamId, completeWrites[0].StreamId.Value);
    }

    [Fact(Timeout = 5000)]
    public void Cleanup_should_be_idempotent()
    {
        var ops = new FakeServerOps();
        var sm = CreateSM(ops);

        const long streamId = 12;
        SendRequest(sm, streamId);

        // First cleanup
        sm.Cleanup();

        // Second cleanup should not crash
        sm.Cleanup();

        Assert.Single(ops.Requests);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void FlushAllPendingRequests_should_emit_pending()
    {
        var ops = new FakeServerOps();
        var sm = CreateSM(ops);

        const long streamId = 16;
        var data = BuildRequest("GET", "/");

        sm.DecodeClientData(new ServerStreamAccepted(StreamTarget.FromId(streamId),
            StreamDirection.Bidirectional));

        var buffer = TransportBuffer.Rent(data.Length);
        data.CopyTo(buffer.FullMemory.Span);
        buffer.Length = data.Length;
        sm.DecodeClientData(MultiplexedData.Rent(buffer, streamId));

        // Request not yet emitted (no StreamReadCompleted)
        Assert.Empty(ops.Requests);

        // Flush all pending
        sm.FlushAllPendingRequests();

        // Now request should be emitted
        Assert.Single(ops.Requests);
        var context = ops.Requests[0];

        var streamIdFeature = context.Get<IHttpStreamIdFeature>();
        Assert.NotNull(streamIdFeature);
        Assert.Equal(streamId, streamIdFeature.StreamId);
    }
}