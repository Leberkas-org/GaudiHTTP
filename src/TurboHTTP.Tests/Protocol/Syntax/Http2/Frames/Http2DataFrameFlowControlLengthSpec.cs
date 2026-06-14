using System.Buffers.Binary;
using TurboHTTP.Protocol.Syntax.Http2;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Frames;

/// <summary>
/// RFC 9113 §6.1: the entire DATA frame payload — including the Pad Length octet and the padding
/// — is counted against flow control, even though only the application data is delivered. The
/// decoder must expose that full length (<see cref="DataFrame.FlowControlledLength"/>) separately
/// from the stripped <see cref="DataFrame.Data"/>.
/// </summary>
public sealed class Http2DataFrameFlowControlLengthSpec
{
    private static byte[] BuildPaddedDataFrame(int streamId, byte[] data, int paddingLength)
    {
        var payloadLength = 1 + data.Length + paddingLength; // 1 byte Pad Length field
        var frame = new byte[9 + payloadLength];
        frame[0] = (byte)(payloadLength >> 16);
        frame[1] = (byte)(payloadLength >> 8);
        frame[2] = (byte)payloadLength;
        frame[3] = 0x00; // DATA
        frame[4] = 0x08; // PADDED
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(5), (uint)streamId);
        frame[9] = (byte)paddingLength;
        Array.Copy(data, 0, frame, 10, data.Length);
        return frame;
    }

    private static byte[] BuildUnpaddedDataFrame(int streamId, byte[] data)
    {
        var frame = new byte[9 + data.Length];
        frame[0] = (byte)(data.Length >> 16);
        frame[1] = (byte)(data.Length >> 8);
        frame[2] = (byte)data.Length;
        frame[3] = 0x00; // DATA
        frame[4] = 0x00; // no flags
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(5), (uint)streamId);
        Array.Copy(data, 0, frame, 9, data.Length);
        return frame;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.1")]
    public void Padded_data_flow_controlled_length_should_include_pad_length_and_padding()
    {
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        const int padding = 100;
        var bytes = BuildPaddedDataFrame(1, data, padding);

        var frame = Assert.IsType<DataFrame>(Assert.Single(new FrameDecoder().Decode(bytes)));

        Assert.Equal(data.Length, frame.Data.Length);
        Assert.Equal(1 + data.Length + padding, frame.FlowControlledLength);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.1")]
    public void Zero_padding_should_still_count_the_pad_length_octet()
    {
        var data = new byte[] { 1, 2, 3 };
        var bytes = BuildPaddedDataFrame(1, data, paddingLength: 0);

        var frame = Assert.IsType<DataFrame>(Assert.Single(new FrameDecoder().Decode(bytes)));

        Assert.Equal(3, frame.Data.Length);
        Assert.Equal(4, frame.FlowControlledLength); // 1 Pad Length octet + 3 data
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.1")]
    public void Unpadded_data_flow_controlled_length_should_equal_data_length()
    {
        var data = new byte[] { 9, 8, 7, 6, 5 };
        var bytes = BuildUnpaddedDataFrame(1, data);

        var frame = Assert.IsType<DataFrame>(Assert.Single(new FrameDecoder().Decode(bytes)));

        Assert.Equal(5, frame.Data.Length);
        Assert.Equal(5, frame.FlowControlledLength);
    }
}
