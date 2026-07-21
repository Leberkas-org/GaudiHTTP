using System.Buffers;
using GaudiHTTP.Protocol.Syntax.Http3;
using GaudiHTTP.Tests.Protocol.Syntax;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http3.Frames;

/// <summary>
/// Contract of the zero-copy <c>DecodeAll(ReadOnlyMemory&lt;byte&gt;)</c> API: payloads of
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

        var sequence = new ReadOnlySequence<byte>(wire.AsMemory());
        var frames = decoder.DecodeAll(sequence, out var consumed);

        Assert.Equal(wire.Length, sequence.GetOffset(consumed));
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
    public void Frame_spanning_two_segments_should_own_its_payload()
    {
        var decoder = new FrameDecoder();
        var wire = SerializeDataFrame(0x42, 256);

        var firstHalf = wire[..(wire.Length / 2)];
        var secondHalf = wire[(wire.Length / 2)..];

        // Decode from a multi-segment sequence in one call
        var sequence = SequenceHelper.CreateMultiSegment(firstHalf, secondHalf);
        var frames = decoder.DecodeAll(sequence, out var consumed);
        var data = Assert.IsType<DataFrame>(Assert.Single(frames));

        Assert.Equal(sequence.Length, sequence.GetOffset(consumed));

        // The multi-segment frame must be an owned copy: scribbling over both inputs must not
        // corrupt the payload.
        Array.Fill(firstHalf, (byte)0xFF);
        Array.Fill(secondHalf, (byte)0xFF);

        Assert.Equal(256, data.Data.Length);
        Assert.True(data.Data.Span.IndexOfAnyExcept((byte)0x42) < 0,
            "Multi-segment frame payload aliases a reused input buffer.");
    }
}
