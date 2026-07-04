using Akka.Actor;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Body;
using GaudiHTTP.Protocol.Syntax.Http3;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http3;

public sealed class Http3OutboundWriterSpec
{
    private sealed class FakeTarget : IMultiplexedBodyDrainTarget
    {
        public List<(long StreamId, byte[] Data, bool EndStream)> Emitted { get; } = [];
        public List<long> Completed { get; } = [];
        public List<(long StreamId, Exception Reason)> Failed { get; } = [];
        public List<object> PendingMessages { get; } = [];
        public IActorRef StageActor => _interceptor;
        private readonly MessageInterceptor _interceptor;

        public FakeTarget()
        {
            _interceptor = new MessageInterceptor(PendingMessages);
        }

        public void EmitDataFrames(long streamId, ReadOnlyMemory<byte> data, bool endStream)
        {
            Emitted.Add((streamId, data.ToArray(), endStream));
        }

        public void OnDrainComplete(long streamId) => Completed.Add(streamId);
        public void OnDrainFailed(long streamId, Exception reason) => Failed.Add((streamId, reason));
    }

    private sealed class MessageInterceptor : MinimalActorRef
    {
        private readonly List<object> _messages;
        public MessageInterceptor(List<object> messages) => _messages = messages;
        public override ActorPath Path { get; } = new RootActorPath(new Address("akka", "test")) / "fake-mux";
        public override IActorRefProvider Provider => throw new NotSupportedException();
        protected override void TellInternal(object message, IActorRef sender) => _messages.Add(message);
    }

    private static MemoryStream MakeBody(int size)
    {
        var data = new byte[size];
        for (var i = 0; i < size; i++)
        {
            data[i] = (byte)(i % 256);
        }

        return new MemoryStream(data);
    }

    private static void DriveReadsWithoutCapacity(
        Http3OutboundWriter writer, FakeTarget target, int maxIterations = 10_000)
    {
        var iterations = 0;
        while (target.PendingMessages.Count > 0 && iterations++ < maxIterations)
        {
            var msg = target.PendingMessages[0];
            target.PendingMessages.RemoveAt(0);
            switch (msg)
            {
                case BodyReadComplete<long> rc:
                    writer.HandleReadComplete(rc.StreamId, rc.BytesRead);
                    break;
            }
        }
    }

    private static void DrainToCompletion(
        Http3OutboundWriter writer, FakeTarget target, int expectedCompletions = 1, int maxIterations = 10_000)
    {
        var iterations = 0;
        while (target.Completed.Count < expectedCompletions
               && target.Failed.Count == 0
               && iterations++ < maxIterations)
        {
            writer.OnCapacityAvailable();

            if (target.PendingMessages.Count == 0)
            {
                break;
            }

            var msg = target.PendingMessages[0];
            target.PendingMessages.RemoveAt(0);
            switch (msg)
            {
                case BodyReadComplete<long> rc:
                    writer.HandleReadComplete(rc.StreamId, rc.BytesRead);
                    break;
            }
        }
    }

    [Fact(Timeout = 5000)]
    public void BuildControlPreface_should_produce_valid_wire_format_with_configured_settings()
    {
        var preface = Http3OutboundWriter.BuildControlPreface(
            qpackMaxTableCapacity: 4096, qpackBlockedStreams: 16, maxFieldSectionSize: 65536);

        try
        {
            Assert.Equal(CriticalStreamId.Control, preface.StreamId);

            var span = preface.Buffer.Memory.Span;
            var streamType = QuicVarInt.Decode(span, out var read);
            Assert.Equal((long)StreamType.Control, streamType);

            var decoder = new FrameDecoder();
            var frames = decoder.DecodeAll(span[read..], out _);
            var settingsFrame = Assert.Single(frames.OfType<SettingsFrame>());

            var parsed = settingsFrame.Parameters.ToDictionary(p => p.Identifier, p => p.Value);
            Assert.Equal(4096, parsed[SettingsIdentifier.QpackMaxTableCapacity]);
            Assert.Equal(16, parsed[SettingsIdentifier.QpackBlockedStreams]);
            Assert.Equal(65536, parsed[SettingsIdentifier.MaxFieldSectionSize]);
        }
        finally
        {
            preface.Buffer.Dispose();
        }
    }

    [Fact(Timeout = 5000)]
    public void EmitDataFrame_should_produce_valid_H3_DATA_frame_wire_format()
    {
        var body = new byte[128];
        new Random(7).NextBytes(body);
        var emitted = new List<ITransportOutbound>();

        Http3OutboundWriter.EmitDataFrame(emitted.Add, streamId: 4, body);

        var item = Assert.Single(emitted.OfType<MultiplexedData>());
        Assert.Equal(4, item.StreamId);

        var decoder = new FrameDecoder();
        var frames = decoder.DecodeAll(item.Buffer.Memory.Span, out _);
        var dataFrame = Assert.Single(frames.OfType<DataFrame>());
        Assert.Equal(body, dataFrame.Data.ToArray());

        item.Buffer.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void EmitDataFrame_should_be_noop_for_empty_body()
    {
        var emitted = new List<ITransportOutbound>();

        Http3OutboundWriter.EmitDataFrame(emitted.Add, streamId: 1, ReadOnlyMemory<byte>.Empty);

        Assert.Empty(emitted);
    }

    [Fact(Timeout = 5000)]
    public void Register_should_drain_body_and_emit_endStream_when_capacity_is_granted()
    {
        var target = new FakeTarget();
        var writer = new Http3OutboundWriter(target, new CancellationTokenSource(), bodyChunkSize: 16 * 1024);

        writer.Register(1L, MakeBody(100), CancellationToken.None);
        DrainToCompletion(writer, target);

        Assert.Equal(2, target.Emitted.Count);
        Assert.Equal(100, target.Emitted[0].Data.Length);
        Assert.False(target.Emitted[0].EndStream);
        Assert.True(target.Emitted[1].EndStream);
        Assert.Single(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void Register_should_bound_in_flight_emissions_to_OutboundBodyCapacity_without_drain_signal()
    {
        var target = new FakeTarget();
        // Body large enough to need far more chunks than the credit cap allows.
        var writer = new Http3OutboundWriter(target, new CancellationTokenSource(), bodyChunkSize: 16);

        writer.Register(1L, MakeBody(16 * (Http3OutboundWriter.OutboundBodyCapacity * 4)), CancellationToken.None);
        DriveReadsWithoutCapacity(writer, target);

        var emittedData = target.Emitted.Count(e => !e.EndStream);
        Assert.True(
            emittedData <= Http3OutboundWriter.OutboundBodyCapacity,
            $"expected at most {Http3OutboundWriter.OutboundBodyCapacity} in-flight emissions without a drain signal, got {emittedData}");
        Assert.Empty(target.Completed);
    }

    [Fact(Timeout = 5000)]
    public void HandleReadFailed_should_report_failure_and_Cancel_should_be_a_noop_after_completion()
    {
        var target = new FakeTarget();
        var writer = new Http3OutboundWriter(target, new CancellationTokenSource(), bodyChunkSize: 16 * 1024);

        writer.Register(1L, MakeBody(10), CancellationToken.None);
        DrainToCompletion(writer, target);
        Assert.Single(target.Completed);

        writer.Cancel(1L);
        Assert.Single(target.Completed);
        Assert.Empty(target.Failed);
    }

    [Fact(Timeout = 5000)]
    public void Cleanup_should_be_idempotent_and_safe_before_any_Register()
    {
        var target = new FakeTarget();
        var writer = new Http3OutboundWriter(target, new CancellationTokenSource(), bodyChunkSize: 16 * 1024);

        writer.Cleanup();
        writer.Cleanup();
    }
}
