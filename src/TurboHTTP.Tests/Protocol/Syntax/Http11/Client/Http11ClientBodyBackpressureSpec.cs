using Servus.Akka.Transport;
using TurboHTTP.Client;
using TurboHTTP.Protocol.Syntax.Http11.Client;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http11.Client;

/// <summary>
/// Regression spec for the H1.1 client request body pump: without a high-water mark the
/// pump copied the entire body into pooled chunks ahead of the socket, so concurrent
/// uploads queued their full bodies in rentals (benchmark: ~1 MB allocated per 1 MB POST
/// at CL=512 vs 93 KB at CL=1). The pump must pause once chunks pile up unflushed and
/// resume on <c>OnOutboundFlushed</c>.
/// </summary>
public sealed class Http11ClientBodyBackpressureSpec
{
    private const int ChunkSize = 16 * 1024;
    private const int BodySize = 1024 * 1024;

    /// <summary>Body stream that records how many reads the pump has issued.</summary>
    private sealed class CountingStream(int length) : Stream
    {
        private int _position;

        public int ReadsIssued { get; private set; }
        public int LastReadSize { get; private set; }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReadsIssued++;
            var n = Math.Min(buffer.Length, length - _position);
            buffer.Span[..n].Fill(0x42);
            _position += n;
            LastReadSize = n;
            return ValueTask.FromResult(n);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
            => ReadAsync(buffer.AsMemory(offset, count)).Result;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static (Http11ClientStateMachine Sm, FakeClientOps Ops, CountingStream Body) CreatePostedRequest()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(ops, new TurboClientOptions
        {
            RequestBodyChunkSize = ChunkSize,
        });
        sm.PreStart();

        var body = new CountingStream(BodySize);
        var content = new StreamContent(body);
        content.Headers.ContentLength = BodySize;
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/upload")
        {
            Version = new Version(1, 1),
            Content = content,
        };

        sm.OnRequest(request);
        return (sm, ops, body);
    }

    /// <summary>Feeds completed reads back into the SM, mimicking the PipeTo message loop.</summary>
    private static void PumpCompletedReads(Http11ClientStateMachine sm, CountingStream body, ref int fed)
    {
        while (body.ReadsIssued > fed)
        {
            fed++;
            sm.OnBodyMessage(new Http11ClientStateMachine.BodyReadComplete(body.LastReadSize));
        }
    }

    [Fact(Timeout = 5000)]
    public void Body_pump_should_pause_when_outbound_is_not_flushed()
    {
        var (sm, ops, body) = CreatePostedRequest();

        // Drive the read/complete loop without ever signalling a flush. The pump must
        // stop issuing reads after a bounded number of unflushed chunks instead of
        // copying the whole 1 MB body into pooled buffers.
        var fed = 0;
        PumpCompletedReads(sm, body, ref fed);

        var bodyChunks = ops.Outbound.OfType<TransportData>().Count() - 1;
        Assert.True(bodyChunks < 8,
            $"Pump emitted {bodyChunks} chunks ({BodySize / ChunkSize} total) without any flush signal — no backpressure.");
        Assert.True(body.ReadsIssued < 8,
            $"Pump issued {body.ReadsIssued} reads without any flush signal — no backpressure.");
    }

    [Fact(Timeout = 5000)]
    public void Body_pump_should_resume_on_flush_and_complete_body()
    {
        var (sm, ops, body) = CreatePostedRequest();

        var fed = 0;
        PumpCompletedReads(sm, body, ref fed);

        // Alternate flush signals and read completions until the body is fully sent.
        var guard = 0;
        while (fed <= BodySize / ChunkSize && guard++ < 10 * BodySize / ChunkSize)
        {
            sm.OnOutboundFlushed();
            PumpCompletedReads(sm, body, ref fed);
        }

        var totalBodyBytes = ops.Outbound.OfType<TransportData>().Skip(1).Sum(d => (long)d.Buffer.Length);
        Assert.Equal(BodySize, totalBodyBytes);
        Assert.True(sm.CanAcceptRequest, "Request should be dispatchable again after body completion.");
    }
}
