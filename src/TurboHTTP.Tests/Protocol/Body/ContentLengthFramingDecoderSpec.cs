using TurboHTTP.Protocol.Body;

namespace TurboHTTP.Tests.Protocol.Body;

public sealed class ContentLengthFramingDecoderSpec
{
    [Fact(Timeout = 5000)]
    public void Decode_should_return_exact_bytes_when_data_matches_remaining()
    {
        var decoder = new ContentLengthFramingDecoder();
        decoder.Reset(5);

        var result = decoder.Decode("hello"u8, out var consumed);

        Assert.Equal(5, consumed);
        Assert.Equal("hello"u8.ToArray(), result.Body.ToArray());
        Assert.True(result.EndOfBody);
        Assert.True(decoder.IsComplete);
    }

    [Fact(Timeout = 5000)]
    public void Decode_should_consume_only_remaining_bytes_when_excess_data()
    {
        var decoder = new ContentLengthFramingDecoder();
        decoder.Reset(3);

        var result = decoder.Decode("helloextra"u8, out var consumed);

        Assert.Equal(3, consumed);
        Assert.Equal("hel"u8.ToArray(), result.Body.ToArray());
        Assert.True(result.EndOfBody);
    }

    [Fact(Timeout = 5000)]
    public void Decode_should_track_remaining_across_calls()
    {
        var decoder = new ContentLengthFramingDecoder();
        decoder.Reset(5);

        var r1 = decoder.Decode("he"u8, out var c1);
        Assert.Equal(2, c1);
        Assert.False(r1.EndOfBody);

        var r2 = decoder.Decode("llo"u8, out var c2);
        Assert.Equal(3, c2);
        Assert.True(r2.EndOfBody);
    }

    [Fact(Timeout = 5000)]
    public void Reset_should_allow_reuse()
    {
        var decoder = new ContentLengthFramingDecoder();
        decoder.Reset(2);
        decoder.Decode("ab"u8, out _);
        Assert.True(decoder.IsComplete);

        decoder.Reset(3);
        Assert.False(decoder.IsComplete);
        decoder.Decode("xyz"u8, out _);
        Assert.True(decoder.IsComplete);
    }

    [Fact(Timeout = 5000)]
    public void Drain_should_discard_bytes_without_returning_body()
    {
        var decoder = new ContentLengthFramingDecoder();
        decoder.Reset(10);

        Assert.Equal(5, decoder.Drain("hello"u8));
        Assert.False(decoder.IsComplete);
        Assert.Equal(5, decoder.Drain("world"u8));
        Assert.True(decoder.IsComplete);
    }
}
