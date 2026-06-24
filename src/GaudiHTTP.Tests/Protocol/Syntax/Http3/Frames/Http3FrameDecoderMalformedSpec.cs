using GaudiHTTP.Protocol;
using GaudiHTTP.Protocol.Syntax.Http3;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http3.Frames;

/// <summary>
/// A frame whose declared length is satisfied but whose body is shorter than the frame type
/// requires (e.g. a varint that needs more bytes than the payload contains) must surface as an
/// <see cref="HttpProtocolException"/> — a classified RFC 9114 §7.1 frame error — not a raw
/// <see cref="ArgumentException"/> that escapes the connection's protocol-error handling.
/// </summary>
public sealed class Http3FrameDecoderMalformedSpec
{
    [Theory]
    [Trait("RFC", "RFC9114-7.1")]
    [InlineData((byte)FrameType.GoAway)]
    [InlineData((byte)FrameType.CancelPush)]
    [InlineData((byte)FrameType.MaxPushId)]
    public void FrameDecoder_should_reject_empty_varint_payload_with_protocol_exception(byte frameType)
    {
        // Declared length 0, but the frame body requires a varint → truncated.
        var bytes = new byte[] { frameType, 0x00 };
        using var decoder = new FrameDecoder();

        Assert.Throws<HttpProtocolException>(() => decoder.DecodeAll(bytes, out _));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.1")]
    public void FrameDecoder_should_reject_settings_with_truncated_value_varint()
    {
        // SETTINGS, declared length 2: identifier 0x00 (1 byte) then a value prefix 0x40 that
        // announces a 2-byte varint but only 1 byte remains → truncated value.
        var bytes = new byte[] { (byte)FrameType.Settings, 0x02, 0x00, 0x40 };
        using var decoder = new FrameDecoder();

        Assert.Throws<HttpProtocolException>(() => decoder.DecodeAll(bytes, out _));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.1")]
    public void FrameDecoder_should_reject_push_promise_with_truncated_push_id()
    {
        // PUSH_PROMISE, declared length 1: a single 0x40 byte announces a 2-byte push-id varint
        // but only 1 byte is present → truncated.
        var bytes = new byte[] { (byte)FrameType.PushPromise, 0x01, 0x40 };
        using var decoder = new FrameDecoder();

        Assert.Throws<HttpProtocolException>(() => decoder.DecodeAll(bytes, out _));
    }
}
