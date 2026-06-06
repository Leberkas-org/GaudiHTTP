using System.Buffers;
using TurboHTTP.Protocol.Body;

namespace TurboHTTP.Tests.Protocol.Body;

public sealed class PassthroughFramingEncoderSpec
{
    [Fact(Timeout = 5000)]
    public void Frame_should_return_exact_data_slice()
    {
        var encoder = new PassthroughFramingEncoder();
        var owner = MemoryPool<byte>.Shared.Rent(16);
        "hello"u8.CopyTo(owner.Memory.Span);

        var framed = encoder.Frame(owner, headroom: 0, dataLength: 5);

        Assert.Equal(5, framed.Length);
        Assert.Equal("hello"u8.ToArray(), framed.ToArray());
        owner.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void Headroom_and_trailer_should_be_zero()
    {
        var encoder = new PassthroughFramingEncoder();
        Assert.Equal(0, encoder.Headroom);
        Assert.Equal(0, encoder.Trailer);
    }

    [Fact(Timeout = 5000)]
    public void GetTerminator_should_return_empty()
    {
        var encoder = new PassthroughFramingEncoder();
        Assert.True(encoder.GetTerminator().IsEmpty);
    }
}
