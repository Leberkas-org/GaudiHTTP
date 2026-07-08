using GaudiHTTP.Protocol;

namespace GaudiHTTP.Tests.Protocol;

/// <summary>
/// RFC 9110 §9.2.2: a request whose body cannot be replayed must not be re-sent with a short body.
/// The protocol encoders read the body via <see cref="HttpContent.ReadAsStream()"/>, which caches
/// and returns the SAME stream instance — after the first attempt it sits at EOF. This is the
/// connection-level guard the H1.1/H2/H3 reconnect replay paths use to rewind a seekable body before
/// re-sending it, or to reject a consumed forward-only body.
/// </summary>
public sealed class RequestBodyReplaySpec
{
    /// <summary>Forward-only body stream: CanSeek == false.</summary>
    private sealed class NonSeekableStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 0;
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => 0;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-9.2.2")]
    public void TryRewindForReplay_should_return_true_for_request_without_body()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        Assert.True(RequestBodyReplay.TryRewindForReplay(request));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-9.2.2")]
    public void TryRewindForReplay_should_rewind_consumed_seekable_body_to_start()
    {
        var payload = new byte[4096];
        var content = new ByteArrayContent(payload);
        var request = new HttpRequestMessage(HttpMethod.Put, "http://example.com/") { Content = content };

        // Simulate the first send having consumed the cached content stream to EOF.
        var stream = content.ReadAsStream();
        var scratch = new byte[payload.Length];
        var read = stream.Read(scratch, 0, scratch.Length);
        Assert.Equal(payload.Length, read);
        Assert.Equal(stream.Length, stream.Position);

        Assert.True(RequestBodyReplay.TryRewindForReplay(request));

        // The SAME cached stream is now rewound to the start, so a replay re-reads the full body.
        Assert.Equal(0, content.ReadAsStream().Position);
        Assert.Equal(payload.Length, content.ReadAsStream().Read(scratch, 0, scratch.Length));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-9.2.2")]
    public void TryRewindForReplay_should_return_false_for_non_rewindable_body()
    {
        var content = new StreamContent(new NonSeekableStream());
        var request = new HttpRequestMessage(HttpMethod.Put, "http://example.com/") { Content = content };

        Assert.False(RequestBodyReplay.TryRewindForReplay(request));
    }
}
