using Servus.Akka.Transport;
using GaudiHTTP.Client;
using GaudiHTTP.Protocol.Body;
using GaudiHTTP.Protocol.Syntax.Http3;
using GaudiHTTP.Protocol.Syntax.Http3.Client;
using GaudiHTTP.Tests.Shared;
using GaudiHTTP.Tests.TestSupport;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http3.Client;

public sealed class Http3FrameBatchingSpec
{
    private static Http3ClientSessionManager CreateSession(FakeClientOps ops)
    {
        var encoderOpts = ClientOptionDefaults.Http3Encoder();
        var decoderOpts = ClientOptionDefaults.Http3Decoder();
        var clientOpts = new GaudiClientOptions { DangerousAcceptAnyServerCertificate = true };
        var session = new Http3ClientSessionManager(encoderOpts, decoderOpts, clientOpts, ops);
        session.OnTransportConnected();
        return session;
    }

    private static List<Http3Frame> DecodeDataFrames(FakeClientOps ops, long streamId)
    {
        var decoder = new FrameDecoder();
        var frames = new List<Http3Frame>();
        foreach (var item in ops.Outbound)
        {
            if (item is MultiplexedData md && md.StreamId == streamId)
            {
                frames.AddRange(decoder.DecodeAll(md.Buffer.Memory.Span, out _).ToList());
                md.Buffer.Dispose();
            }
        }

        return frames;
    }

    [Fact(Timeout = 5000)]
    public void EncodeRequest_should_emit_single_MultiplexedData_for_headeronly_request()
    {
        var ops = new FakeClientOps();
        var session = CreateSession(ops);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/test");
        session.EncodeRequest(request);

        var requestDataItems = ops.Outbound.OfType<MultiplexedData>().Where(md => md.StreamId >= 0).ToList();
        Assert.Single(requestDataItems);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.1")]
    public void EmitDataFrames_should_emit_single_MultiplexedData_buffer_per_chunk()
    {
        var ops = new FakeClientOps();
        var session = CreateSession(ops);
        var target = (IMultiplexedBodyDrainTarget)session;

        const long streamId = 0;
        var body = new byte[256];
        new Random(42).NextBytes(body);

        ops.Outbound.Clear();
        target.EmitDataFrames(streamId, body, endStream: false);

        var dataItems = ops.Outbound.OfType<MultiplexedData>()
            .Where(md => md.StreamId == streamId)
            .ToList();

        Assert.Single(dataItems);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.1")]
    public void EmitDataFrames_should_produce_valid_H3_DATA_frame_wire_format()
    {
        var ops = new FakeClientOps();
        var session = CreateSession(ops);
        var target = (IMultiplexedBodyDrainTarget)session;

        const long streamId = 4;
        var body = new byte[128];
        new Random(7).NextBytes(body);

        ops.Outbound.Clear();
        target.EmitDataFrames(streamId, body, endStream: false);

        var frames = DecodeDataFrames(ops, streamId);
        var dataFrames = frames.OfType<DataFrame>().ToList();

        Assert.Single(dataFrames);
        Assert.Equal(body, dataFrames[0].Data.ToArray());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.1")]
    public void EmitDataFrames_with_endStream_should_emit_CompleteWrites()
    {
        var ops = new FakeClientOps();
        var session = CreateSession(ops);
        var target = (IMultiplexedBodyDrainTarget)session;

        const long streamId = 8;
        var body = new byte[64];
        new Random(3).NextBytes(body);

        ops.Outbound.Clear();
        target.EmitDataFrames(streamId, body, endStream: true);

        var completeWrites = ops.Outbound.OfType<CompleteWrites>().ToList();
        Assert.Single(completeWrites);
        Assert.Equal(streamId, completeWrites[0].StreamId.Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.1")]
    public void EmitDataFrames_with_empty_body_and_endStream_should_emit_only_CompleteWrites()
    {
        var ops = new FakeClientOps();
        var session = CreateSession(ops);
        var target = (IMultiplexedBodyDrainTarget)session;

        const long streamId = 12;

        ops.Outbound.Clear();
        target.EmitDataFrames(streamId, ReadOnlyMemory<byte>.Empty, endStream: true);

        var dataItems = ops.Outbound.OfType<MultiplexedData>()
            .Where(md => md.StreamId == streamId)
            .ToList();
        var completeWrites = ops.Outbound.OfType<CompleteWrites>().ToList();

        Assert.Empty(dataItems);
        Assert.Single(completeWrites);
    }
}