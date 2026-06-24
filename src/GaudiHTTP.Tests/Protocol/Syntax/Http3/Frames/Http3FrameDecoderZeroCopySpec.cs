using TurboHTTP.Protocol.Syntax.Http3;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Frames;

/// <summary>
/// Contract of the zero-copy <c>DecodeAll(ReadOnlyMemory&lt;byte&gt;)</c> overload: payloads of
/// frames fully contained in the input alias the input buffer (no pooled copy); frames
/// assembled from a buffered remainder own their payload and survive input reuse.
/// </summary>
public sealed class Http3FrameDecoderZeroCopySpec
{
    private static byte[] SerializeDataFrame(byte fill, int size)
    {
        var payload = new byte[size];
        Array.Fill(payload, fill);
        var frame = new DataFrame(payload);
        var buf = new byte[frame.SerializedSize];
        var span = buf.AsSpan();
        frame.WriteTo(ref span);
        return buf;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.1")]
    public void Memory_overload_should_return_payload_slices_of_the_input()
    {
        var decoder = new FrameDecoder();
        var wire = SerializeDataFrame(0x11, 256);

        var frames = decoder.DecodeAll(wire.AsMemory(), out var consumed);

        Assert.Equal(wire.Length, consumed);
        var data = Assert.IsType<DataFrame>(Assert.Single(frames));
        Assert.Equal(256, data.Data.Length);
        Assert.True(data.Data.Span.IndexOfAnyExcept((byte)0x11) < 0, "Payload content mismatch.");

        // Zero-copy contract: mutating the input buffer is visible through the frame.
        Array.Fill(wire, (byte)0x99);
        Assert.True(data.Data.Span.IndexOfAnyExcept((byte)0x99) < 0,
            "DATA payload does not alias the input buffer — an unnecessary copy was made.");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.1")]
    public void Frame_spanning_two_inputs_should_own_its_payload()
    {
        var decoder = new FrameDecoder();
        var wire = SerializeDataFrame(0x42, 256);

        var firstHalf = wire[..(wire.Length / 2)];
        var secondHalf = wire[(wire.Length / 2)..];

        var none = decoder.DecodeAll(firstHalf.AsMemory(), out _);
        Assert.Empty(none);

        var frames = decoder.DecodeAll(secondHalf.AsMemory(), out _);
        var data = Assert.IsType<DataFrame>(Assert.Single(frames));

        // The split frame must be an owned copy: scribbling over both inputs must not
        // corrupt the payload.
        Array.Fill(firstHalf, (byte)0xFF);
        Array.Fill(secondHalf, (byte)0xFF);

        Assert.Equal(256, data.Data.Length);
        Assert.True(data.Data.Span.IndexOfAnyExcept((byte)0x42) < 0,
            "Split-frame payload aliases a reused input buffer.");
        (data as IDisposable).Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.1")]
    public void Span_overload_should_keep_copy_semantics()
    {
        var decoder = new FrameDecoder();
        var wire = SerializeDataFrame(0x33, 64);

        var frames = decoder.DecodeAll(wire.AsSpan(), out _);
        var data = Assert.IsType<DataFrame>(Assert.Single(frames));

        Array.Fill(wire, (byte)0xEE);
        Assert.True(data.Data.Span.IndexOfAnyExcept((byte)0x33) < 0,
            "Span-based DecodeAll no longer copies — existing callers rely on copy semantics.");
        (data as IDisposable).Dispose();
    }
}
