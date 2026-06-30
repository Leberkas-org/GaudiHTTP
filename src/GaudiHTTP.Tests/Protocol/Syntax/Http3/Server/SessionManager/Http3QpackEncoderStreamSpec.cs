using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Syntax.Http3;
using GaudiHTTP.Protocol.Syntax.Http3.Qpack;
using GaudiHTTP.Protocol.Syntax.Http3.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Server.Context.Features;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http3.Server.SessionManager;

/// <summary>
/// Regression spec for the HTTP/3 server discarding inbound QPACK encoder-stream instructions.
/// With a non-zero QPACK dynamic table capacity, a request HEADERS block that references the
/// dynamic table blocks in the decoder. The server must apply the encoder-stream instructions,
/// resolve the blocked stream, and dispatch the request — not drop the instructions (which left
/// the request blocked forever until a 30s timeout / RST).
/// </summary>
public sealed class Http3QpackEncoderStreamSpec
{
    private static Http3ConnectionOptions QpackEnabledOptions() => new()
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
        QpackMaxTableCapacity = 4 * 1024,
        QpackBlockedStreams = 10,
        BodyConsumptionTimeout = TimeSpan.FromSeconds(30),
        UseHuffman = true,
    };

    private static void Feed(Http3ServerSessionManager sm, ReadOnlyMemory<byte> bytes, long streamId)
    {
        var buffer = TransportBuffer.Rent(bytes.Length);
        bytes.Span.CopyTo(buffer.FullMemory.Span);
        buffer.Length = bytes.Length;
        sm.DecodeClientData(MultiplexedData.Rent(buffer, streamId));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.3")]
    public void Inbound_qpack_encoder_instructions_should_resolve_a_blocked_request()
    {
        var ops = new FakeServerOps();
        var sm = new Http3ServerSessionManager(QpackEnabledOptions(), ops);
        sm.PreStart();

        // A reference peer encoder that uses the dynamic table: produces (a) encoder-stream
        // insert instructions and (b) a request HEADERS block with Required Insert Count > 0.
        var peer = new QpackTableSync(
            encoderMaxCapacity: 4 * 1024, decoderMaxCapacity: 4 * 1024,
            maxBlockedStreams: 10, configuredEncoderLimit: null);
        var requestHeaders = new List<(string, string)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "localhost"),
            ("x-id", "abc"),
        };
        var headerBlock = peer.Encoder.Encode(requestHeaders);
        var encoderInstructions = peer.Encoder.EncoderInstructions.ToArray();
        Assert.NotEmpty(encoderInstructions);

        const long streamId = 0;
        var headerFrame = new HeadersFrame(headerBlock);
        var frameBytes = new byte[headerFrame.SerializedSize];
        var span = frameBytes.AsSpan();
        headerFrame.WriteTo(ref span);

        // Open the request stream, deliver the (blocked) HEADERS and the QUIC FIN.
        sm.DecodeClientData(new ServerStreamAccepted(StreamTarget.FromId(streamId),
            StreamDirection.Bidirectional));
        Feed(sm, frameBytes, streamId);
        sm.DecodeClientData(new StreamReadCompleted(StreamTarget.FromId(streamId)));

        // The HEADERS reference dynamic entries not yet inserted: the request must not dispatch.
        Assert.Empty(ops.Requests);
        Assert.False(sm.ShouldComplete);

        // The encoder-stream instructions arrive and unblock the stream.
        Feed(sm, encoderInstructions, CriticalStreamId.QpackEncoderId);

        Assert.False(sm.ShouldComplete);
        var request = Assert.Single(ops.Requests);
        var requestFeature = request.Get<IHttpRequestFeature>() as GaudiHttpRequestFeature;
        Assert.NotNull(requestFeature);
        Assert.Equal("GET", requestFeature.Method);
        Assert.True(requestFeature.Headers.TryGetValue("x-id", out var idValue));
        Assert.Equal("abc", idValue);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.3")]
    public void Applying_encoder_instructions_should_emit_insert_count_increment()
    {
        var ops = new FakeServerOps();
        var sm = new Http3ServerSessionManager(QpackEnabledOptions(), ops);
        sm.PreStart();

        var peer = new QpackTableSync(
            encoderMaxCapacity: 4 * 1024, decoderMaxCapacity: 4 * 1024,
            maxBlockedStreams: 10, configuredEncoderLimit: null);
        var headerBlock = peer.Encoder.Encode(new List<(string, string)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "localhost"),
            ("x-id", "abc"),
        });
        var encoderInstructions = peer.Encoder.EncoderInstructions.ToArray();

        const long streamId = 0;
        var headerFrame = new HeadersFrame(headerBlock);
        var frameBytes = new byte[headerFrame.SerializedSize];
        var span = frameBytes.AsSpan();
        headerFrame.WriteTo(ref span);

        sm.DecodeClientData(new ServerStreamAccepted(StreamTarget.FromId(streamId),
            StreamDirection.Bidirectional));
        Feed(sm, frameBytes, streamId);
        sm.DecodeClientData(new StreamReadCompleted(StreamTarget.FromId(streamId)));

        ops.Outbound.Clear();
        Feed(sm, encoderInstructions, CriticalStreamId.QpackEncoderId);

        // After applying inserts the decoder must acknowledge them on its QPACK decoder stream.
        var decoderStreamData = ops.Outbound.OfType<MultiplexedData>()
            .Where(m => m.StreamId == CriticalStreamId.QpackDecoderId)
            .ToList();
        Assert.NotEmpty(decoderStreamData);
    }
}
