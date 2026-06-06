using TurboHTTP.Protocol.Body;

namespace TurboHTTP.Tests.Protocol.Body;

public sealed class CloseDelimitedFramingDecoderSpec
{
    [Fact(Timeout = 5000)]
    public void Decode_should_pass_all_bytes_through()
    {
        var decoder = new CloseDelimitedFramingDecoder();
        decoder.Reset(long.MaxValue);

        var result = decoder.Decode("hello"u8, out var consumed);

        Assert.Equal(5, consumed);
        Assert.Equal("hello"u8.ToArray(), result.Body.ToArray());
        Assert.False(result.EndOfBody);
        Assert.False(decoder.IsComplete);
    }

    [Fact(Timeout = 5000)]
    public void OnEof_should_mark_complete()
    {
        var decoder = new CloseDelimitedFramingDecoder();
        decoder.Reset(long.MaxValue);
        decoder.Decode("data"u8, out _);

        Assert.True(decoder.OnEof());
        Assert.True(decoder.IsComplete);
    }

    [Fact(Timeout = 5000)]
    public void Decode_should_reject_body_exceeding_limit()
    {
        var decoder = new CloseDelimitedFramingDecoder();
        decoder.Reset(5);

        decoder.Decode("hello"u8, out _);

        Assert.Throws<HttpProtocolException>(() => decoder.Decode("x"u8, out _));
    }
}
