namespace GaudiHTTP.Protocol.Syntax.Http3.Qpack;

internal static class QpackDecoderInstructionWriter
{
    public static int WriteSectionAcknowledgment(int streamId, ref SpanWriter writer)
    {
        if (streamId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(streamId), "Stream ID must be non-negative.");
        }

        return QpackIntegerCodec.Encode(streamId, 7, 0x80, ref writer);
    }

    public static int WriteStreamCancellation(int streamId, ref SpanWriter writer)
    {
        if (streamId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(streamId), "Stream ID must be non-negative.");
        }

        return QpackIntegerCodec.Encode(streamId, 6, 0x40, ref writer);
    }

    public static int WriteInsertCountIncrement(int increment, ref SpanWriter writer)
    {
        if (increment <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(increment), "Increment must be positive.");
        }

        return QpackIntegerCodec.Encode(increment, 6, 0x00, ref writer);
    }
}
