using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Syntax.Http2;
using GaudiHTTP.Protocol.Syntax.Http2.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Server.SessionManager;

public sealed class Http2ConnectionErrorTeardownSpec
{
    private static Http2ServerSessionManager CreateSessionManager(FakeServerOps ops)
    {
        var options = new GaudiServerOptions();
        return new Http2ServerSessionManager(options.ToHttp2Options(), ops);
    }

    private static TransportBuffer WrapFrame(byte[] frame)
    {
        var buffer = TransportBuffer.Rent(frame.Length);
        frame.CopyTo(buffer.FullMemory.Span);
        buffer.Length = frame.Length;
        return buffer;
    }

    private static TransportData? FindFrame(FakeServerOps ops, FrameType type) =>
        ops.Outbound.OfType<TransportData>().FirstOrDefault(td => td.Buffer.Span[3] == (byte)type);

    private static int ReadGoAwayErrorCode(TransportData goAway)
    {
        var s = goAway.Buffer.Span;
        return (s[13] << 24) | (s[14] << 16) | (s[15] << 8) | s[16];
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.4.1")]
    public void Connection_protocol_error_should_emit_goaway_and_request_completion()
    {
        var ops = new FakeServerOps();
        var sm = CreateSessionManager(ops);
        sm.PreStart();
        ops.Outbound.Clear();

        // Bare CONTINUATION with no preceding HEADERS is a connection error (RFC 9113 §6.10).
        var frame = new byte[9];
        frame[3] = (byte)FrameType.Continuation;
        frame[8] = 1;
        sm.DecodeClientData(WrapFrame(frame));

        Assert.True(sm.ShouldComplete);
        var goAway = FindFrame(ops, FrameType.GoAway);
        Assert.NotNull(goAway);
        Assert.Equal((int)Http2ErrorCode.ProtocolError, ReadGoAwayErrorCode(goAway));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.3")]
    public void Hpack_decode_error_should_emit_goaway_with_compression_error()
    {
        var ops = new FakeServerOps();
        var sm = CreateSessionManager(ops);
        sm.PreStart();
        ops.Outbound.Clear();

        // HEADERS with END_HEADERS whose HPACK payload is an indexed field referencing index 0 (invalid).
        var payload = new byte[] { 0x80 };
        var frame = new byte[9 + payload.Length];
        frame[2] = (byte)payload.Length;
        frame[3] = (byte)FrameType.Headers;
        frame[4] = 0x04; // END_HEADERS
        frame[8] = 1;
        payload.CopyTo(frame.AsSpan(9));
        sm.DecodeClientData(WrapFrame(frame));

        Assert.True(sm.ShouldComplete);
        var goAway = FindFrame(ops, FrameType.GoAway);
        Assert.NotNull(goAway);
        Assert.Equal((int)Http2ErrorCode.CompressionError, ReadGoAwayErrorCode(goAway));
    }
}
