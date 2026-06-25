using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Body;
using GaudiHTTP.Protocol.Syntax.Http3;
using GaudiHTTP.Protocol.Syntax.Http3.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http3.Server.SessionManager;

public sealed class Http3DataFrameBatchingSpec
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

    private static List<Http3Frame> DecodeDataFrames(FakeServerOps ops, long streamId)
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
    [Trait("RFC", "RFC9114-7.2.1")]
    public void EmitDataFrames_should_emit_single_MultiplexedData_buffer_per_chunk()
    {
        var ops = new FakeServerOps();
        var sm = new Http3ServerSessionManager(DefaultConnectionOptions(), ops);
        var target = (IMultiplexedBodyDrainTarget)sm;

        const long streamId = 4;
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
        var ops = new FakeServerOps();
        var sm = new Http3ServerSessionManager(DefaultConnectionOptions(), ops);
        var target = (IMultiplexedBodyDrainTarget)sm;

        const long streamId = 8;
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
    public void EmitDataFrames_should_preserve_all_bytes_for_large_chunk()
    {
        var ops = new FakeServerOps();
        var sm = new Http3ServerSessionManager(DefaultConnectionOptions(), ops);
        var target = (IMultiplexedBodyDrainTarget)sm;

        const long streamId = 12;
        var body = new byte[1 * 1024 * 1024];
        new Random(99).NextBytes(body);

        ops.Outbound.Clear();
        target.EmitDataFrames(streamId, body, endStream: false);

        var frames = DecodeDataFrames(ops, streamId);
        var dataFrames = frames.OfType<DataFrame>().ToList();

        Assert.Single(dataFrames);
        Assert.Equal(body, dataFrames[0].Data.ToArray());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.1")]
    public void EmitDataFrames_with_empty_data_should_emit_nothing()
    {
        var ops = new FakeServerOps();
        var sm = new Http3ServerSessionManager(DefaultConnectionOptions(), ops);
        var target = (IMultiplexedBodyDrainTarget)sm;

        const long streamId = 16;

        ops.Outbound.Clear();
        target.EmitDataFrames(streamId, ReadOnlyMemory<byte>.Empty, endStream: false);

        var dataItems = ops.Outbound.OfType<MultiplexedData>()
            .Where(md => md.StreamId == streamId)
            .ToList();

        Assert.Empty(dataItems);
    }
}
