using System.Net;
using Servus.Akka.Transport;
using GaudiHTTP.Client;
using GaudiHTTP.Protocol.Syntax.Http3;
using GaudiHTTP.Protocol.Syntax.Http3.Client;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http3;

/// <summary>
/// Per-stream outbound back-pressure for the HTTP/3 client (Task 5 + Task 7). Verifies that the
/// <see cref="MultiplexedDataFlushed"/> decode case credits ONLY the flushed stream's body pump, so
/// starving one stream's credit parks that stream alone while a credited sibling drains to
/// completion — and that crediting the starved stream resumes exactly it.
/// </summary>
public sealed class Http3ClientBodyBackpressureSpec
{
    private const int Chunk = 16 * 1024;                 // client default request-body chunk
    private const int Budget = 256 * 1024;               // Http3OutboundWriter.OutboundBodyByteBudget
    private const int BodyTotal = Budget + 128 * 1024;   // larger than one budget so the stream must park
    private const int BigCredit = 4 * 1024 * 1024;

    private static readonly ConnectionInfo DummyConnectionInfo = new(
        new IPEndPoint(IPAddress.Loopback, 5000),
        new IPEndPoint(IPAddress.Loopback, 443),
        TransportProtocol.Tcp);

    // Body of a known total length exposed only through a non-seekable, non-MemoryStream stream, so
    // EncodeRequest skips every fast path (memory-stream, serialized-direct, empty) and registers the
    // body with the multiplexed pump — the path under test.
    private sealed class StreamingContent(long total) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new NotSupportedException();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false; // unknown length -> no Content-Length header
        }

        protected override Stream CreateContentReadStream(CancellationToken cancellationToken) =>
            new FiniteReadStream(total);
    }

    private sealed class FiniteReadStream(long total) : Stream
    {
        private long _remaining = total;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_remaining <= 0)
            {
                return new ValueTask<int>(0);
            }

            var n = (int)Math.Min(buffer.Length, _remaining);
            buffer.Span[..n].Fill(0x5A);
            _remaining -= n;
            return new ValueTask<int>(n);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_remaining <= 0)
            {
                return 0;
            }

            var n = (int)Math.Min(count, _remaining);
            buffer.AsSpan(offset, n).Fill(0x5A);
            _remaining -= n;
            return n;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static HttpRequestMessage BuildPost(long total) =>
        new(HttpMethod.Post, "https://example.com/upload") { Content = new StreamingContent(total) };

    private static Http3ClientStateMachine CreateConnectedMachine(FakeClientOps ops)
    {
        var sm = new Http3ClientStateMachine(new GaudiClientOptions(), ops);
        sm.PreStart();
        sm.DecodeServerData(new TransportConnected(DummyConnectionInfo));
        return sm;
    }

    /// <summary>
    /// Processes queued body-read completions, crediting each listed stream a real per-stream flush
    /// each turn. A stream NOT listed is starved: it drains only its initial per-stream budget, then
    /// parks. Credit is delivered exactly as the transport does it — via the decode switch.
    /// </summary>
    private static void Drive(
        Http3ClientStateMachine sm, FakeClientOps ops, long[] creditStreams, int maxIterations = 20_000)
    {
        var iterations = 0;
        do
        {
            // Credit first: a real per-stream flush can un-park a stream that currently has no
            // pending read completion (so the queue would otherwise be empty), which is exactly the
            // resume path under test.
            foreach (var s in creditStreams)
            {
                sm.DecodeServerData(new MultiplexedDataFlushed(StreamTarget.FromId(s), BigCredit));
            }

            if (ops.BodyMessages.Count == 0)
            {
                break;
            }

            var msg = ops.BodyMessages[0];
            ops.BodyMessages.RemoveAt(0);
            sm.OnBodyMessage(msg);
        }
        while (iterations++ < maxIterations);
    }

    private static int DataBytes(FakeClientOps ops, long streamId)
    {
        var decoder = new FrameDecoder();
        var total = 0;
        foreach (var item in ops.Outbound)
        {
            if (item is MultiplexedData md && md.StreamId == streamId)
            {
                foreach (var frame in decoder.DecodeAll(md.Buffer.Memory, out _).ToList())
                {
                    if (frame is DataFrame df)
                    {
                        total += df.Data.Length;
                    }
                }
            }
        }

        return total;
    }

    private static bool Completed(FakeClientOps ops, long streamId) =>
        ops.Outbound.Any(o => o is CompleteWrites cw && cw.StreamId.Value == streamId);

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Starving_one_stream_credit_should_park_only_it_while_the_other_drains()
    {
        var ops = new FakeClientOps();
        var sm = CreateConnectedMachine(ops);

        sm.OnRequest(BuildPost(BodyTotal));   // stream 0 (A)
        sm.OnRequest(BuildPost(BodyTotal));   // stream 4 (B)

        // Credit ONLY B via real per-stream flush; A gets nothing beyond its initial budget.
        Drive(sm, ops, [4L]);

        // A parked at (or below) its initial per-stream budget and never reached end-of-stream.
        Assert.True(DataBytes(ops, 0) <= Budget,
            $"stream A should park at its {Budget}-byte budget, emitted {DataBytes(ops, 0)}");
        Assert.False(Completed(ops, 0), "starved stream A must NOT complete");

        // B, continuously credited, drained fully and closed its write side.
        Assert.Equal(BodyTotal, DataBytes(ops, 4));
        Assert.True(Completed(ops, 4), "credited stream B should drain to completion");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void MultiplexedDataFlushed_should_resume_only_the_flushed_stream()
    {
        var ops = new FakeClientOps();
        var sm = CreateConnectedMachine(ops);

        sm.OnRequest(BuildPost(BodyTotal));   // stream 0 (A)
        sm.OnRequest(BuildPost(BodyTotal));   // stream 4 (B)

        Drive(sm, ops, [4L]);

        var aBefore = DataBytes(ops, 0);
        var bBefore = DataBytes(ops, 4);
        Assert.True(aBefore <= Budget);
        Assert.True(Completed(ops, 4));

        // Real per-stream flush for A only: A resumes and drains; B (already done) emits nothing more.
        Drive(sm, ops, [0L]);

        Assert.True(DataBytes(ops, 0) > aBefore, "stream A must resume once its flush credits arrive");
        Assert.Equal(BodyTotal, DataBytes(ops, 0));
        Assert.True(Completed(ops, 0), "stream A should drain to completion after being credited");
        Assert.Equal(bBefore, DataBytes(ops, 4));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void MultiplexedDataFlushed_for_unknown_stream_should_be_a_safe_noop()
    {
        var ops = new FakeClientOps();
        var sm = CreateConnectedMachine(ops);

        // No body streams registered — a stray flush must not throw.
        var ex = Record.Exception(() =>
            sm.DecodeServerData(new MultiplexedDataFlushed(StreamTarget.FromId(999), Chunk)));

        Assert.Null(ex);
    }
}
