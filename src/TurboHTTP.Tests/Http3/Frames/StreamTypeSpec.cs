using TurboHTTP.Protocol.Http3;

namespace TurboHTTP.Tests.Http3.Frames;

public sealed class StreamTypeSpec
{
    [Theory]
    [Trait("RFC", "RFC9114-6.2")]
    [InlineData(StreamType.Control, 0x00L)]
    [InlineData(StreamType.Push, 0x01L)]
    [InlineData(StreamType.QpackEncoder, 0x02L)]
    [InlineData(StreamType.QpackDecoder, 0x03L)]
    public void StreamType_HasCorrectValue(StreamType type, long expected)
    {
        Assert.Equal(expected, (long)type);
    }

    [Fact]
    [Trait("RFC", "RFC9114-6.2")]
    public void AllStreamTypes_AreDefined()
    {
        var values = Enum.GetValues<StreamType>();
        Assert.Equal(4, values.Length);
    }

    [Fact]
    [Trait("RFC", "RFC9114-6.2")]
    public void ControlStream_IsZero()
    {
        Assert.Equal(0x00L, (long)StreamType.Control);
    }

    [Fact]
    [Trait("RFC", "RFC9114-6.2")]
    public void PushStream_IsOne()
    {
        Assert.Equal(0x01L, (long)StreamType.Push);
    }

    [Fact]
    [Trait("RFC", "RFC9114-6.2")]
    public void QpackEncoderStream_IsTwo()
    {
        Assert.Equal(0x02L, (long)StreamType.QpackEncoder);
    }

    [Fact]
    [Trait("RFC", "RFC9114-6.2")]
    public void QpackDecoderStream_IsThree()
    {
        Assert.Equal(0x03L, (long)StreamType.QpackDecoder);
    }
}
