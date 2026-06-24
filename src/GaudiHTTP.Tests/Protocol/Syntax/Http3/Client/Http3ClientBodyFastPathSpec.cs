using Servus.Akka.Transport;
using GaudiHTTP.Client;
using GaudiHTTP.Protocol.Syntax.Http3;
using GaudiHTTP.Protocol.Syntax.Http3.Client;
using GaudiHTTP.Tests.Shared;
using GaudiHTTP.Tests.TestSupport;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http3.Client;

public sealed class Http3ClientBodyFastPathSpec
{
    // A custom HttpContent whose ReadAsStream() returns a publicly-visible MemoryStream,
    // exactly the pattern the fast path is designed for.
    private sealed class VisibleMemoryStreamContent : HttpContent
    {
        private readonly MemoryStream _ms;

        public VisibleMemoryStreamContent(byte[] body)
        {
            _ms = new MemoryStream();
            _ms.Write(body);
            Headers.ContentLength = body.Length;
        }

        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
            _ms.CopyToAsync(stream);

        protected override bool TryComputeLength(out long length)
        {
            length = _ms.Length;
            return true;
        }

        protected override Stream CreateContentReadStream(CancellationToken cancellationToken)
        {
            _ms.Position = 0;
            return _ms;
        }
    }

    private static Http3ClientSessionManager CreateSession(FakeClientOps ops)
    {
        var encoderOpts = ClientOptionDefaults.Http3Encoder();
        var decoderOpts = ClientOptionDefaults.Http3Decoder();
        var clientOpts = new GaudiClientOptions { DangerousAcceptAnyServerCertificate = true };
        var sm = new Http3ClientSessionManager(encoderOpts, decoderOpts, clientOpts, ops);
        sm.OnTransportConnected();
        return sm;
    }

    private static List<Http3Frame> DecodeOutboundData(FakeClientOps ops, long streamId)
    {
        var decoder = new FrameDecoder();
        var frames = new List<Http3Frame>();
        foreach (var item in ops.Outbound)
        {
            if (item is MultiplexedData md && md.StreamId == streamId)
            {
                // ToList so the reused decoder buffer is copied before the next decode call
                frames.AddRange(decoder.DecodeAll(md.Buffer.Memory.Span, out _).ToList());
                md.Buffer.Dispose();
            }
        }

        return frames;
    }

    private static HttpRequestMessage BuildPost(byte[] body)
    {
        return new HttpRequestMessage(HttpMethod.Post, "https://example.com/upload")
        {
            Content = new VisibleMemoryStreamContent(body)
        };
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Visible_MemoryStream_body_should_emit_single_DATA_frame()
    {
        var ops = new FakeClientOps();
        var sm = CreateSession(ops);

        var body = new byte[256];
        new Random(42).NextBytes(body);
        var request = BuildPost(body);

        sm.EncodeRequest(request);

        // Stream 0 is the first bidirectional request stream
        var frames = DecodeOutboundData(ops, streamId: 0);
        var dataFrames = frames.OfType<DataFrame>().ToList();

        Assert.Single(dataFrames);
        Assert.Equal(body, dataFrames[0].Data.ToArray());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Fast_path_should_emit_CompleteWrites_after_DATA_frame()
    {
        var ops = new FakeClientOps();
        var sm = CreateSession(ops);

        var body = new byte[64];
        new Random(1).NextBytes(body);
        var request = BuildPost(body);

        sm.EncodeRequest(request);

        var completeWrites = ops.Outbound.OfType<CompleteWrites>().ToList();
        Assert.NotEmpty(completeWrites);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Fast_path_should_preserve_all_bytes_for_large_payload()
    {
        var ops = new FakeClientOps();
        var sm = CreateSession(ops);

        // 1 MiB — verifies no truncation
        var body = new byte[1 * 1024 * 1024];
        new Random(55).NextBytes(body);
        var request = BuildPost(body);

        sm.EncodeRequest(request);

        var frames = DecodeOutboundData(ops, streamId: 0);
        var dataFrames = frames.OfType<DataFrame>().ToList();

        var assembled = dataFrames.SelectMany(f => f.Data.ToArray()).ToArray();
        Assert.Equal(body, assembled);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Non_visible_MemoryStream_should_fall_through_to_encoder_slow_path()
    {
        // ByteArrayContent.ReadAsStream() returns MemoryStream with TryGetBuffer=false.
        // Verify EncodeRequest does not throw and falls back to the encoder.
        var ops = new FakeClientOps();
        var sm = CreateSession(ops);

        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/upload")
        {
            Content = new ByteArrayContent(new byte[] { 1, 2, 3 })
        };

        // Should not throw — falls back to encoder
        var exception = Record.Exception(() => sm.EncodeRequest(request));
        Assert.Null(exception);
    }

    // A custom HttpContent that overrides SerializeToStream synchronously (fast path B target).
    // Its ReadAsStream() returns a non-visible MemoryStream so the TryGetBuffer fast path A
    // does not trigger, exercising the SerializeToStream code path instead.
    private sealed class SyncSerializableContent : HttpContent
    {
        private readonly byte[] _body;

        public SyncSerializableContent(byte[] body)
        {
            _body = body;
            Headers.ContentLength = body.Length;
        }

        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
            stream.WriteAsync(_body).AsTask();

        protected override void SerializeToStream(Stream stream, System.Net.TransportContext? context, CancellationToken cancellationToken) =>
            stream.Write(_body);

        protected override bool TryComputeLength(out long length)
        {
            length = _body.Length;
            return true;
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void SerializeToStream_fast_path_should_emit_single_DATA_frame_for_sync_content()
    {
        var ops = new FakeClientOps();
        var sm = CreateSession(ops);

        var body = new byte[200];
        new Random(77).NextBytes(body);

        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/upload")
        {
            Content = new SyncSerializableContent(body)
        };

        sm.EncodeRequest(request);

        var frames = DecodeOutboundData(ops, streamId: 0);
        var dataFrames = frames.OfType<DataFrame>().ToList();

        Assert.Single(dataFrames);
        Assert.Equal(body, dataFrames[0].Data.ToArray());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void SerializeToStream_fast_path_should_emit_CompleteWrites_after_DATA_frame()
    {
        var ops = new FakeClientOps();
        var sm = CreateSession(ops);

        var body = new byte[64];
        new Random(3).NextBytes(body);

        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/upload")
        {
            Content = new SyncSerializableContent(body)
        };

        sm.EncodeRequest(request);

        var completeWrites = ops.Outbound.OfType<CompleteWrites>().ToList();
        Assert.NotEmpty(completeWrites);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void SerializeToStream_fast_path_should_skip_body_exceeding_buffer_threshold()
    {
        // Body above MaxBufferedRequestBodySize (default 64 KiB) must bypass the fast path
        // and be handed off to the async encoder without throwing.
        var ops = new FakeClientOps();
        var sm = CreateSession(ops);

        var body = new byte[128 * 1024];
        new Random(5).NextBytes(body);

        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/upload")
        {
            Content = new SyncSerializableContent(body)
        };

        var exception = Record.Exception(() => sm.EncodeRequest(request));
        Assert.Null(exception);
    }
}
