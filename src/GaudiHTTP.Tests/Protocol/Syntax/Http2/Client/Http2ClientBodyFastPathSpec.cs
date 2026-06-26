using Servus.Akka.Transport;
using GaudiHTTP.Client;
using GaudiHTTP.Protocol.Syntax.Http2;
using GaudiHTTP.Protocol.Syntax.Http2.Client;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Client;

public sealed class Http2ClientBodyFastPathSpec
{
    // A custom HttpContent whose ReadAsStream() returns a publicly-visible MemoryStream,
    // exactly the pattern the fast path is designed for (e.g. an in-memory body built by
    // serializing into a fresh MemoryStream before sending).
    private sealed class VisibleMemoryStreamContent : HttpContent
    {
        private readonly MemoryStream _ms;

        public VisibleMemoryStreamContent(byte[] body)
        {
            // new MemoryStream() is publicly visible — TryGetBuffer returns true.
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

    private static Http2ClientSessionManager CreateSession(FakeClientOps ops, int initialSendWindow = 1 * 1024 * 1024)
    {
        var options = new GaudiClientOptions
        {
            Http2 = new Http2ClientOptions
            {
                InitialStreamWindowSize = initialSendWindow
            }
        };
        return new Http2ClientSessionManager(options, ops);
    }

    private static List<Http2Frame> DecodeOutbound(FakeClientOps ops)
    {
        var frames = new List<Http2Frame>();
        foreach (var item in ops.Outbound)
        {
            if (item is TransportData { Buffer: var buf })
            {
                // Use a fresh decoder per buffer: the H2 preface magic ("PRI *...") would
                // otherwise leave bytes as remainder and corrupt the next frame parse.
                // Copy frame data before the decoder is disposed (its working buffer is
                // the same TransportBuffer, disposed with the decoder).
                var decoder = new FrameDecoder();
                var decoded = decoder.Decode(buf);
                foreach (var frame in decoded)
                {
                    // Copy the frame's memory slices so they remain valid after Dispose.
                    frames.Add(frame is DataFrame df
                        ? new DataFrame(df.StreamId, df.Data.ToArray(), df.EndStream)
                        : frame);
                }

                decoder.Dispose();
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
    [Trait("RFC", "RFC9113-5.1")]
    public void Visible_MemoryStream_body_should_emit_DATA_frames_inline()
    {
        var ops = new FakeClientOps();
        var sm = CreateSession(ops);

        var body = new byte[100];
        new Random(42).NextBytes(body);
        var request = BuildPost(body);

        sm.EncodeRequest(request);

        var frames = DecodeOutbound(ops);
        var dataFrames = frames.OfType<DataFrame>().ToList();
        Assert.NotEmpty(dataFrames);

        var assembled = dataFrames.SelectMany(f => f.Data.ToArray()).ToArray();
        Assert.Equal(body, assembled);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.1")]
    public void Fast_path_should_split_body_by_MaxFrameSize()
    {
        var ops = new FakeClientOps();
        var sm = CreateSession(ops);

        // Body larger than the RFC default MaxFrameSize of 16 KiB
        var body = new byte[40 * 1024];
        new Random(7).NextBytes(body);
        var request = BuildPost(body);

        sm.EncodeRequest(request);

        var frames = DecodeOutbound(ops);
        var dataFrames = frames.OfType<DataFrame>().ToList();

        // Each frame payload must not exceed 16 KiB (server default MAX_FRAME_SIZE)
        foreach (var frame in dataFrames)
        {
            Assert.True(frame.Data.Length <= 16 * 1024,
                $"DATA frame payload {frame.Data.Length} exceeds MaxFrameSize 16 KiB");
        }

        var assembled = dataFrames.SelectMany(f => f.Data.ToArray()).ToArray();
        Assert.Equal(body, assembled);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Fast_path_should_buffer_remainder_when_send_window_exhausted()
    {
        // Drive the send window down to 256 bytes by faking a server SETTINGS with
        // INITIAL_WINDOW_SIZE = 256. The send window defaults to 65535 (RFC default)
        // and only shrinks when the server sends SETTINGS.
        var ops = new FakeClientOps();
        var sm = CreateSession(ops);

        // Server sends SETTINGS with a tiny INITIAL_WINDOW_SIZE to constrain our send window.
        sm.ProcessFrame(new SettingsFrame(
            [(SettingsParameter.InitialWindowSize, 256u)],
            isAck: false));

        var body = new byte[1024];
        new Random(3).NextBytes(body);
        var request = BuildPost(body);

        sm.EncodeRequest(request);

        var frames = DecodeOutbound(ops);
        var dataFrames = frames.OfType<DataFrame>().Where(f => f.StreamId == 1).ToList();

        // Only the windowed portion (256 bytes) should have been emitted immediately
        var emittedBytes = dataFrames.Sum(f => f.Data.Length);
        Assert.Equal(256, emittedBytes);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Fast_path_should_drain_remainder_on_window_update()
    {
        // Server starts with a tiny INITIAL_WINDOW_SIZE, then opens the window.
        var ops = new FakeClientOps();
        var sm = CreateSession(ops);

        sm.ProcessFrame(new SettingsFrame(
            [(SettingsParameter.InitialWindowSize, 256u)],
            isAck: false));

        var body = new byte[512];
        new Random(11).NextBytes(body);
        var request = BuildPost(body);

        sm.EncodeRequest(request);

        // Grant more window so the remainder can drain
        sm.ProcessFrame(new WindowUpdateFrame(streamId: 0, increment: 1024 * 1024));
        sm.ProcessFrame(new WindowUpdateFrame(streamId: 1, increment: 1024 * 1024));

        for (var guard = 0; guard < 1000 && ops.BodyMessages.Count > 0; guard++)
        {
            var msg = ops.BodyMessages[0];
            ops.BodyMessages.RemoveAt(0);
            sm.OnBodyMessage(msg);
        }

        var frames = DecodeOutbound(ops);
        var dataFrames = frames.OfType<DataFrame>().Where(f => f.StreamId == 1).ToList();
        var assembled = dataFrames.SelectMany(f => f.Data.ToArray()).ToArray();
        Assert.Equal(body, assembled);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
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
    [Trait("RFC", "RFC9113-6.1")]
    public void SerializeToStream_fast_path_should_emit_DATA_frames_for_sync_content()
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

        var frames = DecodeOutbound(ops);
        var dataFrames = frames.OfType<DataFrame>().ToList();

        Assert.NotEmpty(dataFrames);

        var assembled = dataFrames.SelectMany(f => f.Data.ToArray()).ToArray();
        Assert.Equal(body, assembled);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.1")]
    public void SerializeToStream_fast_path_should_split_body_by_MaxFrameSize()
    {
        var ops = new FakeClientOps();
        var sm = CreateSession(ops);

        // Body larger than the RFC default MaxFrameSize of 16 KiB but within the 64 KiB threshold
        var body = new byte[40 * 1024];
        new Random(99).NextBytes(body);

        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/upload")
        {
            Content = new SyncSerializableContent(body)
        };

        sm.EncodeRequest(request);

        var frames = DecodeOutbound(ops);
        var dataFrames = frames.OfType<DataFrame>().ToList();

        foreach (var frame in dataFrames)
        {
            Assert.True(frame.Data.Length <= 16 * 1024,
                $"DATA frame payload {frame.Data.Length} exceeds MaxFrameSize 16 KiB");
        }

        var assembled = dataFrames.SelectMany(f => f.Data.ToArray()).ToArray();
        Assert.Equal(body, assembled);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
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
