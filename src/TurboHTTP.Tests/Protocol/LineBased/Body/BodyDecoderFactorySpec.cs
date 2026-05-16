using System.Buffers;
using TurboHTTP.Protocol.LineBased.Body;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Tests.Protocol.LineBased.Body;

public sealed class BodyDecoderFactorySpec
{
    private const int Threshold = 1024;

    private static IBodyDecoder Create(BodyClassification c)
        => BodyDecoderFactory.Create(c, Threshold, MemoryPool<byte>.Shared);

    [Theory(Timeout = 5000)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1023)]
    [InlineData(1024)]
    public void Factory_should_return_Buffered_when_length_at_or_below_threshold(int len)
    {
        var decoder = Create(new BodyClassification(BodyFraming.Length, len));
        Assert.IsType<ContentLengthBufferedDecoder>(decoder);
        decoder.Dispose();
    }

    [Theory(Timeout = 5000)]
    [InlineData(1025)]
    [InlineData(1_000_000)]
    public void Factory_should_return_Streamed_when_length_above_threshold(int len)
    {
        var decoder = Create(new BodyClassification(BodyFraming.Length, len));
        Assert.IsType<ContentLengthStreamedDecoder>(decoder);
        decoder.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void Factory_should_return_ChunkedDecoder_when_framing_is_Chunked()
    {
        var decoder = Create(new BodyClassification(BodyFraming.Chunked, null));
        Assert.IsType<ChunkedBodyDecoder>(decoder);
        decoder.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void Factory_should_return_CloseDelimited_when_framing_is_Close()
    {
        var decoder = Create(new BodyClassification(BodyFraming.Close, null));
        Assert.IsType<CloseDelimitedBodyDecoder>(decoder);
        decoder.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void Factory_should_return_empty_Buffered_when_framing_is_None()
    {
        var decoder = Create(new BodyClassification(BodyFraming.None, null));
        Assert.IsType<ContentLengthBufferedDecoder>(decoder);
        decoder.Dispose();
    }
}