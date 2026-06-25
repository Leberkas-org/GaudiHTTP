using Servus.Akka.Transport;
using GaudiHTTP.Client;
using GaudiHTTP.Protocol.Syntax.Http11.Client;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http11.Client;

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
        var sm = new Http11ClientStateMachine(ops, new GaudiClientOptions
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

    [Fact(Timeout = 5000)]
    public void Body_pump_should_pause_when_outbound_is_not_flushed()
    {
        var (_, ops, body) = CreatePostedRequest();

        // CountingStream completes reads synchronously, so OnRequest drives the inline pump directly
        // (no mailbox round-trip per chunk). With no flush ever signalled, the pump must stop after a
        // bounded number of unflushed chunks instead of copying the whole 1 MB body into pooled
        // buffers ahead of the socket.
        var bodyChunks = ops.Outbound.OfType<TransportData>().Count() - 1;
        Assert.True(bodyChunks < 8,
            $"Pump emitted {bodyChunks} chunks ({BodySize / ChunkSize} total) without any flush signal — no backpressure.");
        Assert.True(body.ReadsIssued < 8,
            $"Pump issued {body.ReadsIssued} reads without any flush signal — no backpressure.");
    }

    [Fact(Timeout = 5000)]
    public void Body_pump_should_resume_on_flush_and_complete_body()
    {
        var (sm, ops, _) = CreatePostedRequest();

        // The pump paused mid-body (no flush yet). Each flush signal resumes it for one more bounded
        // burst; keep flushing until the entire body has been sent and the connection is dispatchable.
        var guard = 0;
        while (!sm.CanAcceptRequest && guard++ < 10 * (BodySize / ChunkSize))
        {
            sm.OnOutboundFlushed();
            sm.OnBodyMessage(new GaudiHTTP.Protocol.Body.DrainContinue(0));
        }

        var totalBodyBytes = ops.Outbound.OfType<TransportData>().Skip(1).Sum(d => (long)d.Buffer.Length);
        Assert.Equal(BodySize, totalBodyBytes);
        Assert.True(sm.CanAcceptRequest, "Request should be dispatchable again after body completion.");
    }
}
