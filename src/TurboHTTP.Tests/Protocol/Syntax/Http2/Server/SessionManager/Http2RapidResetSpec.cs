using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http2;
using TurboHTTP.Protocol.Syntax.Http2.Server;
using TurboHTTP.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Server.SessionManager;

public sealed class Http2RapidResetSpec
{
    private static byte[] BuildRstStream(int streamId, Http2ErrorCode code = Http2ErrorCode.Cancel)
    {
        var frame = new byte[9 + 4];
        frame[2] = 4; // payload length
        frame[3] = (byte)FrameType.RstStream;
        frame[5] = (byte)(streamId >> 24);
        frame[6] = (byte)(streamId >> 16);
        frame[7] = (byte)(streamId >> 8);
        frame[8] = (byte)streamId;
        var c = (uint)code;
        frame[9] = (byte)(c >> 24);
        frame[10] = (byte)(c >> 16);
        frame[11] = (byte)(c >> 8);
        frame[12] = (byte)c;
        return frame;
    }

    private static TransportBuffer WrapFrame(byte[] frame)
    {
        var buffer = TransportBuffer.Rent(frame.Length);
        frame.CopyTo(buffer.FullMemory.Span);
        buffer.Length = frame.Length;
        return buffer;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void Excessive_stream_resets_should_emit_goaway_enhance_your_calm()
    {
        // CVE-2023-44487 (Rapid Reset): a client that opens-and-resets streams faster than a threshold
        // must be cut off with GOAWAY(ENHANCE_YOUR_CALM); MaxConcurrentStreams alone never saturates.
        var ops = new FakeServerOps();
        var options = new TurboServerOptions { Limits = { MaxResetStreamsPerWindow = 5 } };
        var sm = new Http2ServerSessionManager(options.ToHttp2Options(), ops);
        sm.PreStart();
        ops.Outbound.Clear();

        for (var i = 0; i < 6; i++)
        {
            sm.DecodeClientData(WrapFrame(BuildRstStream(1 + i * 2)));
        }

        Assert.True(sm.ShouldComplete);

        TransportData? goAway = null;
        foreach (var item in ops.Outbound)
        {
            if (item is TransportData td && td.Buffer.Span[3] == (byte)FrameType.GoAway)
            {
                goAway = td;
            }
        }

        Assert.NotNull(goAway);
        var s = goAway!.Buffer.Span;
        var code = (s[13] << 24) | (s[14] << 16) | (s[15] << 8) | s[16];
        Assert.Equal((int)Http2ErrorCode.EnhanceYourCalm, code);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void Resets_below_threshold_should_not_terminate_the_connection()
    {
        var ops = new FakeServerOps();
        var options = new TurboServerOptions { Limits = { MaxResetStreamsPerWindow = 5 } };
        var sm = new Http2ServerSessionManager(options.ToHttp2Options(), ops);
        sm.PreStart();
        ops.Outbound.Clear();

        for (var i = 0; i < 4; i++)
        {
            sm.DecodeClientData(WrapFrame(BuildRstStream(1 + i * 2)));
        }

        Assert.False(sm.ShouldComplete);
    }
}
