using System.Net;
using Servus.Akka.TestKit;
using Servus.Akka.Transport;
using GaudiHTTP.Client;
using GaudiHTTP.Protocol;
using GaudiHTTP.Protocol.Syntax.Http11.Client;
using GaudiHTTP.Tests.Shared;
using GaudiHTTP.Tests.TestSupport;

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
        var transport = new TestPipeTransport();

        var body = new CountingStream(BodySize);
        var content = new StreamContent(body);
        content.Headers.ContentLength = BodySize;
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/upload")
        {
            Version = new Version(1, 1),
            Content = content,
        };

        sm.OnRequest(request);
        sm.DecodeServerData(new TransportConnected(DummyConnectionInfo, transport));
        return (sm, ops, body);
    }

    private static readonly ConnectionInfo DummyConnectionInfo = new(
        new IPEndPoint(IPAddress.Loopback, 5000),
        new IPEndPoint(IPAddress.Loopback, 80),
        TransportProtocol.Tcp);

    private static (Http11ClientStateMachine Sm, FakeClientOps Ops, CountingStream Body) CreateBodiedRequest(
        HttpMethod method, GaudiClientOptions options)
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(options, ops);
        sm.PreStart();

        var body = new CountingStream(BodySize);
        var content = new StreamContent(body);
        content.Headers.ContentLength = BodySize;
        var request = new HttpRequestMessage(method, "http://example.com/upload")
        {
            Version = new Version(1, 1),
            Content = content,
        };

        sm.OnRequest(request);
        sm.DecodeServerData(new TransportConnected(DummyConnectionInfo, new TestPipeTransport()));
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

        DrainBodyMessages(sm, ops);

        Assert.True(body.ReadsIssued <= BudgetChunks + 1,
            $"Pump issued {body.ReadsIssued} reads without any flush signal — no backpressure.");
        Assert.True(body.ReadsIssued < TotalChunks,
            $"Pump issued {body.ReadsIssued} of {TotalChunks} reads without any flush — no byte backpressure.");
    }

    [Fact(Timeout = 5000)]
    public void One_transport_flush_should_release_exactly_one_more_chunk()
    {
        var (sm, ops, body) = CreatePostedRequest();
        DrainBodyMessages(sm, ops);

        var readsBefore = body.ReadsIssued;

        sm.DecodeServerData(new TransportDataFlushed(ChunkSize));
        DrainBodyMessages(sm, ops);

        Assert.Equal(readsBefore + 1, body.ReadsIssued);
    }

    [Fact(Timeout = 5000)]
    public void Body_pump_should_resume_on_transport_flush_and_complete_body()
    {
        var (sm, ops, _) = CreatePostedRequest();

        DrainBodyMessages(sm, ops);

        var guard = 0;
        while (!sm.CanAcceptRequest && guard++ < 10 * TotalChunks)
        {
            sm.DecodeServerData(new TransportDataFlushed(ChunkSize));
            DrainBodyMessages(sm, ops);
        }

        Assert.True(sm.CanAcceptRequest, "Request should be dispatchable again after body completion.");
    }

    [Fact(Timeout = 5000)]
    public void Initial_TransportConnected_should_create_pump_and_complete_body()
    {
        var (sm, ops, body) = CreatePostedRequest();

        DrainBodyMessages(sm, ops);
        Assert.InRange(body.ReadsIssued, 1, TotalChunks - 1);

        var guard = 0;
        while (!sm.CanAcceptRequest && guard++ < 10 * TotalChunks)
        {
            sm.DecodeServerData(new TransportDataFlushed(ChunkSize));
            DrainBodyMessages(sm, ops);
        }

        Assert.True(sm.CanAcceptRequest,
            "Body pump created during initial TransportConnected must complete the upload.");
    }

    [Fact(Timeout = 5000)]
    public void Reconnect_should_tear_down_stale_pump_and_emit_no_stale_bytes()
    {
        var options = TestClientOptions.Create(maxPipelineDepth: 4, http1MaxReconnectAttempts: 3,
            http1ReconnectInitialBackoff: TimeSpan.Zero);
        options.RequestBodyChunkSize = ChunkSize;
        var (sm, ops, body) = CreateBodiedRequest(HttpMethod.Post, options);

        DrainBodyMessages(sm, ops);
        Assert.True(body.ReadsIssued < TotalChunks,
            "Pump should be parked mid-body before the disconnect.");

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));
        Assert.True(sm.IsReconnecting);

        var readsBeforeReconnect = body.ReadsIssued;
        sm.DecodeServerData(new TransportConnected(DummyConnectionInfo, new TestPipeTransport()));
        DrainBodyMessages(sm, ops);

        sm.DecodeServerData(new TransportDataFlushed(ChunkSize));
        sm.DecodeServerData(new TransportDataFlushed(ChunkSize));
        DrainBodyMessages(sm, ops);

        Assert.Equal(readsBeforeReconnect, body.ReadsIssued);
    }
}
