using Akka.Actor;
using Akka.Event;
using Microsoft.Extensions.Time.Testing;
using Servus.Akka.Transport;
using GaudiHTTP.Client;
using GaudiHTTP.Protocol.Body;
using GaudiHTTP.Protocol.Syntax.Http2;
using GaudiHTTP.Protocol.Syntax.Http2.Client;
using GaudiHTTP.Streams.Stages.Client;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Client;

public sealed class Http2ClientBodyFrameBatchingSpec
{
    private sealed class CapturingClientStageOperations : IClientStageOperations
    {
        public int OutboundDataItems { get; private set; }
        public List<(int StreamId, byte[] Payload, bool EndStream)> DataFrames { get; } = [];

        public void OnResponse(HttpResponseMessage response) { }

        public void OnOutbound(ITransportOutbound item)
        {
            if (item is not TransportData { Buffer: var buf })
            {
                return;
            }

            var frames = new FrameDecoder().Decode(buf);
            var sawData = false;
            foreach (var frame in frames)
            {
                if (frame is DataFrame d)
                {
                    // Snapshot the payload: Data spans into the reused transport buffer.
                    DataFrames.Add((d.StreamId, d.Data.ToArray(), d.EndStream));
                    sawData = true;
                }
            }

            if (sawData)
            {
                OutboundDataItems++;
            }
        }

        public void OnScheduleTimer(string name, TimeSpan duration) { }

        public void OnCancelTimer(string name) { }

        public ILoggingAdapter Log => throw new NotImplementedException();

        public IActorRef StageActor => throw new NotImplementedException();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.1")]
    public void EmitDataFrames_should_batch_a_multiframe_body_into_a_single_outbound()
    {
        const int maxFrame = 16 * 1024;
        const int streamId = 7;
        var body = new byte[40000]; // 16384 + 16384 + 7232 -> three DATA frames
        for (var i = 0; i < body.Length; i++)
        {
            body[i] = (byte)(i % 251);
        }

        var ops = new CapturingClientStageOperations();
        var sm = new Http2ClientSessionManager(new GaudiClientOptions(), ops, new FakeTimeProvider());

        ((IBodyDrainTarget)sm).EmitDataFrames(streamId, body, endStream: true);

        // The whole body must arrive in ONE outbound buffer, not one per frame.
        Assert.Equal(1, ops.OutboundDataItems);

        // ...carrying the correct sequence of DATA frames (wire format preserved).
        Assert.Equal(3, ops.DataFrames.Count);
        Assert.Equal([maxFrame, maxFrame, 40000 - 2 * maxFrame], ops.DataFrames.Select(f => f.Payload.Length));
        Assert.All(ops.DataFrames, f => Assert.Equal(streamId, f.StreamId));
        Assert.Equal([false, false, true], ops.DataFrames.Select(f => f.EndStream));

        var reassembled = ops.DataFrames.SelectMany(f => f.Payload).ToArray();
        Assert.Equal(body, reassembled);
    }
}
