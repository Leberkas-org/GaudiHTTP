using System.Text;
using TurboHTTP.Protocol.LineBased.Body;

namespace TurboHTTP.Tests.Protocol.LineBased.Body;

public sealed class CloseDelimitedBodyDecoderSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.3")]
    public async Task Decoder_should_accumulate_until_eof()
    {
        var decoder = new CloseDelimitedBodyDecoder();
        Assert.False(decoder.Feed("part1"u8, out var c1));
        Assert.Equal(5, c1);
        Assert.False(decoder.Feed("part2"u8, out var c2));
        Assert.Equal(5, c2);

        Assert.True(decoder.OnEof());

        var content = Assert.IsType<StreamContent>(decoder.GetContent());
        var bytes = await content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal("part1part2", Encoding.ASCII.GetString(bytes));
        decoder.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void Feed_should_never_return_true()
    {
        var decoder = new CloseDelimitedBodyDecoder();
        Assert.False(decoder.Feed("data"u8, out _));
        decoder.Dispose();
    }
}