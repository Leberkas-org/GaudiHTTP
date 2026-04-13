using TurboHTTP.Protocol.Http3;

namespace TurboHTTP.Tests.Http3.Streams;

public sealed class UniStreamSpec
{
    private static byte[] EncodeStreamType(long type)
    {
        var buf = new byte[QuicVarInt.EncodedLength(type)];
        QuicVarInt.Encode(type, buf);
        return buf;
    }

    [Theory]
    [Trait("RFC", "RFC9114-6.2")]
    [InlineData(0x00L, UniStreamRouting.Control)]
    [InlineData(0x01L, UniStreamRouting.Push)]
    [InlineData(0x02L, UniStreamRouting.QpackEncoder)]
    [InlineData(0x03L, UniStreamRouting.QpackDecoder)]
    public void TryIdentify_KnownTypes_RoutesCorrectly(long typeValue, UniStreamRouting expected)
    {
        var handler = new UniStream();
        var data = EncodeStreamType(typeValue);

        var result = handler.TryIdentify(data, out var routing, out var streamType, out var consumed);

        Assert.True(result);
        Assert.Equal(expected, routing);
        Assert.Equal(typeValue, streamType);
        Assert.Equal(data.Length, consumed);
    }

    [Fact]
    [Trait("RFC", "RFC9114-6.2")]
    public void TryIdentify_UnknownType_ReturnsUnknown()
    {
        var handler = new UniStream();
        // 0x21 is a reserved/unknown stream type (not 0x00-0x03)
        var data = EncodeStreamType(0x21);

        var result = handler.TryIdentify(data, out var routing, out var streamType, out var consumed);

        Assert.True(result);
        Assert.Equal(UniStreamRouting.Unknown, routing);
        Assert.Equal(0x21L, streamType);
        Assert.True(consumed > 0);
    }

    [Theory]
    [Trait("RFC", "RFC9114-6.2")]
    [InlineData(0x21L)]
    [InlineData(0x42L)]
    [InlineData(0xFF_FFL)]
    [InlineData(0x1FL)]
    public void TryIdentify_VariousUnknownTypes_AllIgnored(long unknownType)
    {
        var handler = new UniStream();
        var data = EncodeStreamType(unknownType);

        var result = handler.TryIdentify(data, out var routing, out _, out _);

        Assert.True(result);
        Assert.Equal(UniStreamRouting.Unknown, routing);
    }

    [Fact]
    [Trait("RFC", "RFC9114-6.2")]
    public void TryIdentify_QpackEncoder_TracksState()
    {
        var handler = new UniStream();
        var data = EncodeStreamType((long)StreamType.QpackEncoder);

        Assert.False(handler.QpackEncoderStreamReceived);

        handler.TryIdentify(data, out var routing, out _, out _);

        Assert.Equal(UniStreamRouting.QpackEncoder, routing);
        Assert.True(handler.QpackEncoderStreamReceived);
    }

    [Fact]
    [Trait("RFC", "RFC9114-6.2")]
    public void TryIdentify_QpackDecoder_TracksState()
    {
        var handler = new UniStream();
        var data = EncodeStreamType((long)StreamType.QpackDecoder);

        Assert.False(handler.QpackDecoderStreamReceived);

        handler.TryIdentify(data, out var routing, out _, out _);

        Assert.Equal(UniStreamRouting.QpackDecoder, routing);
        Assert.True(handler.QpackDecoderStreamReceived);
    }

    [Fact]
    [Trait("RFC", "RFC9114-6.2")]
    public void TryIdentify_DuplicateControlStream_Throws()
    {
        var handler = new UniStream();
        var data = EncodeStreamType((long)StreamType.Control);

        handler.TryIdentify(data, out _, out _, out _);

        var ex = Assert.Throws<Http3Exception>(
            () => handler.TryIdentify(data, out _, out _, out _));
        Assert.Equal(Http3ErrorCode.StreamCreationError, ex.ErrorCode);
    }

    [Fact]
    [Trait("RFC", "RFC9114-6.2")]
    public void TryIdentify_DuplicateQpackEncoder_Throws()
    {
        var handler = new UniStream();
        var data = EncodeStreamType((long)StreamType.QpackEncoder);

        handler.TryIdentify(data, out _, out _, out _);

        var ex = Assert.Throws<Http3Exception>(
            () => handler.TryIdentify(data, out _, out _, out _));
        Assert.Equal(Http3ErrorCode.StreamCreationError, ex.ErrorCode);
    }

    [Fact]
    [Trait("RFC", "RFC9114-6.2")]
    public void TryIdentify_DuplicateQpackDecoder_Throws()
    {
        var handler = new UniStream();
        var data = EncodeStreamType((long)StreamType.QpackDecoder);

        handler.TryIdentify(data, out _, out _, out _);

        var ex = Assert.Throws<Http3Exception>(
            () => handler.TryIdentify(data, out _, out _, out _));
        Assert.Equal(Http3ErrorCode.StreamCreationError, ex.ErrorCode);
    }

    [Fact]
    [Trait("RFC", "RFC9114-6.2")]
    public void TryIdentify_EmptyBuffer_ReturnsFalse()
    {
        var handler = new UniStream();

        var result = handler.TryIdentify(ReadOnlySpan<byte>.Empty, out var routing, out var streamType, out var consumed);

        Assert.False(result);
        Assert.Equal(UniStreamRouting.Unknown, routing);
        Assert.Equal(-1L, streamType);
        Assert.Equal(0, consumed);
    }
}
