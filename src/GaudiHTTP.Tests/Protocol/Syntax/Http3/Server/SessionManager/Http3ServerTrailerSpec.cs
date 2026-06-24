using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Syntax.Http3;
using GaudiHTTP.Protocol.Syntax.Http3.Qpack;
using GaudiHTTP.Protocol.Syntax.Http3.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Server.Context;
using GaudiHTTP.Server.Context.Features;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http3.Server.SessionManager;

public sealed class Http3ServerTrailerSpec
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

    /// <summary>
    /// Creates a response feature collection with no body started (takes the no-body/CompleteWrites path).
    /// This is the simplest path to exercise EmitEndOfBody without a drain pump.
    /// </summary>
    private static IFeatureCollection CreateNoBodyResponseWithTrailers(
        long streamId, IHeaderDictionary trailers)
    {
        var features = new GaudiFeatureCollection();
        features.Set<IHttpRequestFeature>(new GaudiHttpRequestFeature());
        features.Set<IHttpResponseFeature>(new GaudiHttpResponseFeature { StatusCode = 200 });
        features.Set<IHttpStreamIdFeature>(new GaudiStreamIdFeature(streamId));
        // No body started — takes the !hasBody branch
        var bodyFeature = new GaudiHttpResponseBodyFeature();
        features.Set<IHttpResponseBodyFeature>(bodyFeature);

        features.Set<IHttpResponseTrailersFeature>(new FakeHttpResponseTrailersFeature(trailers));
        return features;
    }

    private static IFeatureCollection CreateNoBodyResponseNoTrailers(long streamId)
    {
        var features = new GaudiFeatureCollection();
        features.Set<IHttpRequestFeature>(new GaudiHttpRequestFeature());
        features.Set<IHttpResponseFeature>(new GaudiHttpResponseFeature { StatusCode = 200 });
        features.Set<IHttpStreamIdFeature>(new GaudiStreamIdFeature(streamId));
        var bodyFeature = new GaudiHttpResponseBodyFeature();
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        return features;
    }

    private sealed class FakeHttpResponseTrailersFeature(IHeaderDictionary trailers) : IHttpResponseTrailersFeature
    {
        public IHeaderDictionary Trailers { get; set; } = trailers;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void OnResponse_no_body_with_trailers_should_emit_HEADERS_before_CompleteWrites()
    {
        var ops = new FakeServerOps();
        var sm = new Http3ServerSessionManager(DefaultOptions(), ops);

        const long streamId = 4;
        SendRequest(sm, streamId);

        var trailers = new GaudiHeaderDictionary
        {
            { "grpc-status", "0" }
        };
        var context = CreateNoBodyResponseWithTrailers(streamId, trailers);
        ops.Outbound.Clear();
        sm.OnResponse(context);

        // Should have emitted: MultiplexedData (response headers) + MultiplexedData (trailer headers) + CompleteWrites
        var allOutbound = ops.Outbound;
        var multiplexedItems = allOutbound.OfType<MultiplexedData>().ToList();
        var completeWrites = allOutbound.OfType<CompleteWrites>().ToList();

        // At least one MultiplexedData for response headers + one for trailer headers
        Assert.True(multiplexedItems.Count >= 2,
            $"Expected at least 2 MultiplexedData items (response headers + trailer headers), got {multiplexedItems.Count}");
        Assert.Single(completeWrites);
        Assert.Equal(streamId, completeWrites[0].StreamId.Value);

        // The last MultiplexedData (trailer headers) must appear before CompleteWrites
        var lastMultiplexedIndex = allOutbound.IndexOf(multiplexedItems.Last());
        var completeIndex = allOutbound.IndexOf(completeWrites[0]);
        Assert.True(lastMultiplexedIndex < completeIndex,
            "Trailer HEADERS frame must precede CompleteWrites");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void OnResponse_no_body_without_trailers_should_emit_only_CompleteWrites()
    {
        var ops = new FakeServerOps();
        var sm = new Http3ServerSessionManager(DefaultOptions(), ops);

        const long streamId = 4;
        SendRequest(sm, streamId);

        var context = CreateNoBodyResponseNoTrailers(streamId);
        ops.Outbound.Clear();
        sm.OnResponse(context);

        var completeWrites = ops.Outbound.OfType<CompleteWrites>().ToList();
        Assert.Single(completeWrites);
        Assert.Equal(streamId, completeWrites[0].StreamId.Value);

        // Only response headers MultiplexedData + CompleteWrites — no extra trailer frame
        var multiplexedItems = ops.Outbound.OfType<MultiplexedData>().ToList();
        Assert.True(multiplexedItems.Count <= 1,
            "No trailer HEADERS frame should be emitted when there are no trailers");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.5")]
    public void OnResponse_should_freeze_GaudiResponseHeaderDictionary_trailers_before_encoding()
    {
        var ops = new FakeServerOps();
        var sm = new Http3ServerSessionManager(DefaultOptions(), ops);

        const long streamId = 4;
        SendRequest(sm, streamId);

        var trailers = new GaudiHeaderDictionary
        {
            { "grpc-status", "0" }
        };
        var context = CreateNoBodyResponseWithTrailers(streamId, trailers);
        sm.OnResponse(context);

        Assert.True(trailers.IsReadOnly, "Trailer dictionary must be read-only after emission");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void StreamState_should_store_and_retrieve_features()
    {
        var state = new StreamState();
        state.Initialize(42);

        var features = new GaudiFeatureCollection();
        features.Set<IHttpResponseFeature>(new GaudiHttpResponseFeature { StatusCode = 200 });

        state.SetFeatures(features);

        var retrieved = state.GetFeatures();
        Assert.NotNull(retrieved);
        Assert.Same(features, retrieved);

        var responseFeature = retrieved.Get<IHttpResponseFeature>();
        Assert.NotNull(responseFeature);
        Assert.Equal(200, responseFeature.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void StreamState_should_clear_features_on_Reset()
    {
        var state = new StreamState();
        state.Initialize(42);

        var features = new GaudiFeatureCollection();
        state.SetFeatures(features);

        state.Reset();

        Assert.Null(state.GetFeatures());
    }
}
