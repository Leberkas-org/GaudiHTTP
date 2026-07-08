using Servus.Akka.Transport;
using GaudiHTTP.Client;
using GaudiHTTP.Protocol.Syntax.Http11.Client;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http11.Client;

/// <summary>
/// Regression spec for the H1.1 client request body pump: without byte back-pressure the
/// pump copied the entire body into pooled chunks ahead of the socket, so concurrent
/// uploads queued their full bodies in rentals (benchmark: ~1 MB allocated per 1 MB POST
/// at CL=512 vs 93 KB at CL=1). The pump now carries a byte budget (256 KB) and only advances
/// when a real wire flush arrives as a decoded <see cref="TransportDataFlushed"/> — the
/// push-time <c>OnOutboundFlushed</c> "lie" no longer drives it.
/// </summary>
public sealed class Http11ClientBodyBackpressureSpec
{
    private const int ChunkSize = 16 * 1024;
    private const int BodySize = 1024 * 1024;
    private const int MaxBudget = 256 * 1024;
    private const int TotalChunks = BodySize / ChunkSize;
    private const int BudgetChunks = MaxBudget / ChunkSize;

    /// <summary>Body stream that records how many reads the pump has issued.</summary>
    private sealed class CountingStream(int length) : Stream
    {
        private int _position;

        public int ReadsIssued { get; private set; }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReadsIssued++;
            var n = Math.Min(buffer.Length, length - _position);
            buffer.Span[..n].Fill(0x42);
            _position += n;
            return ValueTask.FromResult(n);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            ReadsIssued++;
            var n = Math.Min(count, length - _position);
            buffer.AsSpan(offset, n).Fill(0x42);
            _position += n;
            return n;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static (Http11ClientStateMachine Sm, FakeClientOps Ops, CountingStream Body) CreatePostedRequest()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(new GaudiClientOptions
        {
            RequestBodyChunkSize = ChunkSize,
        }, ops);
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

    private static void DrainBodyMessages(Http11ClientStateMachine sm, FakeClientOps ops)
    {
        while (ops.BodyMessages.Count > 0)
        {
            var msg = ops.BodyMessages[0];
            ops.BodyMessages.RemoveAt(0);
            sm.OnBodyMessage(msg);
        }
    }

    [Fact(Timeout = 5000)]
    public void Body_pump_should_park_at_byte_budget_when_outbound_is_not_flushed()
    {
        var (sm, ops, body) = CreatePostedRequest();

        // Drain every read completion WITHOUT feeding a single TransportDataFlushed. The pump must
        // park once its 256 KB byte budget is exhausted instead of copying the whole 1 MB body into
        // pooled buffers ahead of the socket.
        DrainBodyMessages(sm, ops);

        var bodyChunks = ops.Outbound.OfType<TransportData>().Count() - 1;
        Assert.True(bodyChunks <= BudgetChunks + 1,
            $"Pump emitted {bodyChunks} chunks; expected to park near the {BudgetChunks}-chunk budget without any flush.");
        Assert.True(bodyChunks < TotalChunks,
            $"Pump emitted {bodyChunks} of {TotalChunks} chunks without any flush — no byte backpressure.");
        Assert.True(body.ReadsIssued <= BudgetChunks + 1,
            $"Pump issued {body.ReadsIssued} reads without any flush signal — no backpressure.");
    }

    [Fact(Timeout = 5000)]
    public void One_transport_flush_should_release_exactly_one_more_chunk()
    {
        var (sm, ops, _) = CreatePostedRequest();
        DrainBodyMessages(sm, ops);

        var before = ops.Outbound.OfType<TransportData>().Count();

        // A single real flush of one chunk's worth credits exactly one more chunk.
        sm.DecodeServerData(new TransportDataFlushed(ChunkSize));
        DrainBodyMessages(sm, ops);

        var after = ops.Outbound.OfType<TransportData>().Count();
        Assert.Equal(before + 1, after);
    }

    [Fact(Timeout = 5000)]
    public void Body_pump_should_resume_on_transport_flush_and_complete_body()
    {
        var (sm, ops, _) = CreatePostedRequest();

        // Drain the initial burst up to the byte budget (first park).
        DrainBodyMessages(sm, ops);

        // Each decoded TransportDataFlushed credits the pump for one more bounded burst; keep flushing
        // until the entire body has been sent and the connection is dispatchable again.
        var guard = 0;
        while (!sm.CanAcceptRequest && guard++ < 10 * TotalChunks)
        {
            sm.DecodeServerData(new TransportDataFlushed(ChunkSize));
            DrainBodyMessages(sm, ops);
        }

        var totalBodyBytes = ops.Outbound.OfType<TransportData>().Skip(1).Sum(d => (long)d.Buffer.Length);
        Assert.Equal(BodySize, totalBodyBytes);
        Assert.True(sm.CanAcceptRequest, "Request should be dispatchable again after body completion.");
    }
}
