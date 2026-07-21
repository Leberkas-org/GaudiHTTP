using System.Buffers;
using GaudiHTTP.Protocol.Syntax.Http2;
using GaudiHTTP.Tests.TestSupport;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Client.StateMachine;

public sealed class Http2RstStreamRestrictionSpec
{
    private static byte[] MakeRstStreamBytes(int streamId, Http2ErrorCode errorCode)
        => new RstStreamFrame(streamId, errorCode).Serialize();

    private static byte[] MakeWindowUpdateBytes(int streamId, int increment)
        => new WindowUpdateFrame(streamId, increment).Serialize();

    private static byte[] Concat(params byte[][] arrays)
    {
        var result = new byte[arrays.Sum(a => a.Length)];
        var offset = 0;
        foreach (var arr in arrays)
        {
            arr.CopyTo(result, offset);
            offset += arr.Length;
        }

        return result;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.4")]
    public void FrameDecoder_should_decode_rst_stream_with_correct_error_code()
    {
        var decoder = new FrameDecoder();
        var frames = decoder.DecodeAll(new ReadOnlySequence<byte>(MakeRstStreamBytes(1, Http2ErrorCode.Cancel)), out _);

        var rst = Assert.IsType<RstStreamFrame>(frames[0]);
        Assert.Equal(1, rst.StreamId);
        Assert.Equal(Http2ErrorCode.Cancel, rst.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.4")]
    public void FrameDecoder_should_accept_window_update_after_rst_stream_on_same_stream()
    {
        var decoder = new FrameDecoder();
        var bytes = Concat(
            MakeRstStreamBytes(1, Http2ErrorCode.Cancel),
            MakeWindowUpdateBytes(1, 1024));

        var frames = decoder.DecodeAll(new ReadOnlySequence<byte>(bytes), out _);

        Assert.Equal(2, frames.Count);
        Assert.IsType<RstStreamFrame>(frames[0]);
        Assert.IsType<WindowUpdateFrame>(frames[1]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.4")]
    public void FrameDecoder_should_accept_rst_stream_after_rst_stream_on_same_stream()
    {
        var decoder = new FrameDecoder();
        var bytes = Concat(
            MakeRstStreamBytes(1, Http2ErrorCode.Cancel),
            MakeRstStreamBytes(1, Http2ErrorCode.NoError));

        var frames = decoder.DecodeAll(new ReadOnlySequence<byte>(bytes), out _);

        Assert.Equal(2, frames.Count);
        Assert.IsType<RstStreamFrame>(frames[0]);
        Assert.IsType<RstStreamFrame>(frames[1]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.4")]
    public void FrameDecoder_should_decode_rst_stream_on_stream_zero()
    {
        // RFC 9113 §6.4: RST_STREAM on stream 0 MUST trigger a connection PROTOCOL_ERROR.
        // FrameDecoder produces the frame; stream-0 validation is the caller's responsibility.
        var decoder = new FrameDecoder();
        var frames = decoder.DecodeAll(new ReadOnlySequence<byte>(MakeRstStreamBytes(0, Http2ErrorCode.Cancel)), out _);

        var frame = Assert.IsType<RstStreamFrame>(frames[0]);
        Assert.Equal(0, frame.StreamId);
        Assert.Equal(Http2ErrorCode.Cancel, frame.ErrorCode);
    }
}
