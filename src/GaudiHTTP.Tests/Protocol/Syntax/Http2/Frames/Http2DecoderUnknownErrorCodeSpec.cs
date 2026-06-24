using System.Buffers.Binary;
using TurboHTTP.Protocol.Syntax.Http2;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Frames;

public sealed class Http2DecoderUnknownErrorCodeSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-7")]
    public void Http2FrameDecoder_should_decode_unknown_error_code_in_goaway()
    {
        var frame = new byte[9 + 8];
        frame[2] = 8;
        frame[3] = 0x07;
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(9), 0);
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(13), 0xFFu);

        var decoder = new FrameDecoder();
        var frames = decoder.Decode(frame);

        var goaway = Assert.IsType<GoAwayFrame>(frames[0]);
        Assert.Equal((Http2ErrorCode)0xFF, goaway.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-7")]
    public void Http2FrameDecoder_should_decode_unknown_error_code_in_rst_stream()
    {
        var frame = new byte[9 + 4];
        frame[2] = 4;
        frame[3] = 0x03;
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(5), 1u);
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(9), 0xFEu);

        var decoder = new FrameDecoder();
        var frames = decoder.Decode(frame);

        var rst = Assert.IsType<RstStreamFrame>(frames[0]);
        Assert.Equal(1, rst.StreamId);
        Assert.Equal((Http2ErrorCode)0xFE, rst.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-7")]
    public void Http2FrameDecoder_should_decode_max_uint_error_code_without_throwing()
    {
        var frame = new byte[9 + 8];
        frame[2] = 8;
        frame[3] = 0x07;
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(9), 0);
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(13), 0xFFFFFFFFu);

        var decoder = new FrameDecoder();
        var frames = decoder.Decode(frame);

        var goaway = Assert.IsType<GoAwayFrame>(frames[0]);
        Assert.Equal((Http2ErrorCode)0xFFFFFFFF, goaway.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-7")]
    public void Http2FrameDecoder_should_decode_all_defined_error_codes_without_throwing()
    {
        var definedCodes = new uint[]
        {
            0x0, 0x1, 0x2, 0x3, 0x4, 0x5, 0x6,
            0x7, 0x8, 0x9, 0xa, 0xb, 0xc, 0xd
        };

        var decoder = new FrameDecoder();
        foreach (var code in definedCodes)
        {
            var frame = new GoAwayFrame(0, (Http2ErrorCode)code).Serialize();
            var frames = decoder.Decode(frame);
            var goaway = Assert.IsType<GoAwayFrame>(frames[0]);
            Assert.Equal((Http2ErrorCode)code, goaway.ErrorCode);
        }
    }
}
