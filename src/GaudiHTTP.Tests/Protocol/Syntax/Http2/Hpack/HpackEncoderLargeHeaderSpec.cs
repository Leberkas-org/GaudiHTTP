using TurboHTTP.Protocol.Syntax.Http2.Hpack;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Hpack;

public sealed class HpackEncoderLargeHeaderSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.2")]
    public void HpackEncoder_should_encode_value_larger_than_the_default_buffer()
    {
        // A large cookie/JWT exceeding the encoder's 4096-byte default rent must not overflow the buffer.
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var longValue = new string('x', 8000);
        var headers = new List<(string, string)> { ("x-long", longValue) };

        var encoded = encoder.Encode(headers);
        var decoded = decoder.Decode(encoded.Span);

        Assert.Single(decoded);
        Assert.Equal("x-long", decoded[0].Name);
        Assert.Equal(longValue, decoded[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.2")]
    public void HpackEncoder_should_encode_large_value_with_huffman()
    {
        var encoder = new HpackEncoder(useHuffman: true);
        var decoder = new HpackDecoder();

        var longValue = new string('a', 10000);
        var headers = new List<(string, string)> { ("x-long", longValue) };

        var encoded = encoder.Encode(headers);
        var decoded = decoder.Decode(encoded.Span);

        Assert.Single(decoded);
        Assert.Equal(longValue, decoded[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6")]
    public void HpackEncoder_should_encode_many_headers_exceeding_the_default_buffer()
    {
        // Cumulative header list well past 4096 bytes across many fields.
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var headers = new List<(string, string)>();
        for (var i = 0; i < 50; i++)
        {
            headers.Add(($"x-header-{i}", new string('v', 200)));
        }

        var encoded = encoder.Encode(headers);
        var decoded = decoder.Decode(encoded.Span);

        Assert.Equal(headers.Count, decoded.Count);
        Assert.Equal(headers[^1].Item2, decoded[^1].Value);
    }
}
