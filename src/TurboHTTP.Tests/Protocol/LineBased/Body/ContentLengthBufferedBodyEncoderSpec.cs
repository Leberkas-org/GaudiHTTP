using System.Net;
using System.Text;
using Akka.TestKit.Xunit;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.LineBased.Body;

namespace TurboHTTP.Tests.Protocol.LineBased.Body;

public sealed class ContentLengthBufferedBodyEncoderSpec : TestKit
{
    [Fact(Timeout = 5000)]
    public void Start_should_deliver_body_chunk_then_complete()
    {
        var probe = CreateTestProbe();
        var content = new ByteArrayContent("hello"u8.ToArray());
        using var encoder = new ContentLengthBufferedBodyEncoder();

        var bodyStream = content.ReadAsStream();
        encoder.Start(bodyStream, probe.Ref);

        var msg1 = probe.ReceiveOne(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        var chunk = Assert.IsType<OutboundBodyChunk>(msg1);
        Assert.Equal(5, chunk.Length);
        Assert.Equal("hello", Encoding.UTF8.GetString(chunk.Owner.Memory.Span[..chunk.Length]));
        chunk.Owner.Dispose();

        var msg2 = probe.ReceiveOne(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.IsType<OutboundBodyComplete>(msg2);
    }

    [Fact(Timeout = 5000)]
    public void Start_should_deliver_failed_on_error()
    {
        var probe = CreateTestProbe();
        using var encoder = new ContentLengthBufferedBodyEncoder();

        var bodyStream = new FailingStream();
        encoder.Start(bodyStream, probe.Ref);

        var msg = probe.ReceiveOne(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        var failed = Assert.IsType<OutboundBodyFailed>(msg);
        Assert.NotNull(failed.Reason);
    }

    private sealed class FailingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
            => throw new InvalidOperationException("content error");

        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();

        public override void SetLength(long value)
            => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();
    }
}