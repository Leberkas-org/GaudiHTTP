using GaudiHTTP.Protocol.Syntax.Http3;
using GaudiHTTP.Protocol.Syntax.Http3.Client;
using GaudiHTTP.Protocol.Syntax.Http3.Qpack;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http3.Client;

/// <summary>
/// Regression spec for the HTTP/3 client QPACK-blocked DATA loss: when a response's HEADERS
/// reference dynamic-table entries not yet inserted, the stream blocks. DATA frames that arrive
/// for that stream before the QPACK encoder instructions unblock it must be buffered and replayed
/// once the headers resolve — not silently dropped (which truncated the response body).
/// </summary>
public sealed class Http3QpackBlockedDataSpec
{
    private static (StreamManager Manager, QpackTableSync Sync) CreateManager(FakeClientOps ops)
    {
        var sync = new QpackTableSync(
            encoderMaxCapacity: 4 * 1024, decoderMaxCapacity: 4 * 1024,
            maxBlockedStreams: 10, configuredEncoderLimit: null);
        var decoder = new Http3ClientDecoder(sync, maxFieldSectionSize: 16 * 1024);
        var manager = new StreamManager(ops, decoder, sync, maxResponseBodySize: long.MaxValue);
        return (manager, sync);
    }

    /// <summary>Encodes a 200 response whose custom header forces a QPACK dynamic insert (RIC &gt; 0).</summary>
    private static ReadOnlyMemory<byte> EncodeBlockingHeaders(QpackTableSync sync)
        => sync.Encoder.Encode(new List<(string, string)>
        {
            (":status", "200"),
            ("x-dynamic", "blocked-value"),
        });

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-2.1.2")]
    public async Task DATA_received_while_HEADERS_qpack_blocked_should_be_replayed_after_resolution()
    {
        var ops = new FakeClientOps();
        var (mgr, sync) = CreateManager(ops);

        const long streamId = 4;
        var body = new byte[] { 10, 20, 30, 40, 50 };

        mgr.Correlate(streamId, new HttpRequestMessage(HttpMethod.Get, "https://localhost/"));

        // HEADERS reference the dynamic table but the encoder instructions have not arrived:
        // the stream blocks and no response is emitted yet.
        var headerBlock = EncodeBlockingHeaders(sync);
        mgr.AssembleResponse(new HeadersFrame(headerBlock), streamId);
        Assert.Empty(ops.Responses);

        // DATA arrives while still blocked. Pre-fix this was dropped → truncated body.
        mgr.AssembleResponse(new DataFrame(body), streamId);

        // Encoder instructions arrive on the QPACK encoder stream and unblock the headers.
        sync.ProcessEncoderInstructions(sync.Encoder.EncoderInstructions.Span);
        mgr.ResolveBlockedStreams(sync.ResolveBlockedStreams());

        // QUIC FIN on the request stream completes the body.
        mgr.FlushPendingResponse(streamId);

        var response = Assert.Single(ops.Responses);
        var received = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(body, received);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-2.1.2")]
    public async Task Stream_FIN_received_while_qpack_blocked_should_still_complete_the_body()
    {
        var ops = new FakeClientOps();
        var (mgr, sync) = CreateManager(ops);

        const long streamId = 8;
        var body = new byte[] { 1, 2, 3 };

        mgr.Correlate(streamId, new HttpRequestMessage(HttpMethod.Get, "https://localhost/"));

        var headerBlock = EncodeBlockingHeaders(sync);
        mgr.AssembleResponse(new HeadersFrame(headerBlock), streamId);
        mgr.AssembleResponse(new DataFrame(body), streamId);

        // The QUIC FIN arrives BEFORE the QPACK encoder instructions (legal stream interleaving).
        mgr.FlushPendingResponse(streamId);
        Assert.Empty(ops.Responses);

        // Now the headers resolve: the response must emit AND the deferred FIN must complete the body.
        sync.ProcessEncoderInstructions(sync.Encoder.EncoderInstructions.Span);
        mgr.ResolveBlockedStreams(sync.ResolveBlockedStreams());

        var response = Assert.Single(ops.Responses);
        var received = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(body, received);
    }
}
