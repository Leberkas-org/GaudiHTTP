using System.Buffers;
using GaudiHTTP.Pooling;

namespace GaudiHTTP.Protocol.Syntax.Http3;

/// <summary>
/// Stateful HTTP/3 frame decoder per RFC 9114 §7.
/// Accepts <see cref="ReadOnlySequence{T}"/> and returns a <see cref="SequencePosition"/>
/// indicating how far into the input frames were fully decoded. Unconsumed trailing
/// bytes are NOT buffered internally — the caller (or Pipe) retains them via AdvanceTo.
/// Unknown frame types are skipped gracefully per RFC 9114 §7.2.8.
/// The returned list is reused on every call — callers MUST fully consume it
/// before the next DecodeAll and MUST NOT retain it.
/// </summary>
internal sealed class FrameDecoder : Poolable<FrameDecoder>
{
    private readonly List<Http3Frame> _frames = [];

    public IReadOnlyList<Http3Frame> DecodeAll(in ReadOnlySequence<byte> input, out SequencePosition consumed)
    {
        _frames.Clear();

        if (input.IsEmpty)
        {
            consumed = input.Start;
            return _frames;
        }

        if (input.IsSingleSegment)
        {
            consumed = DecodeFromSpan(input.First, input);
        }
        else
        {
            consumed = DecodeFromSequence(input);
        }

        return _frames;
    }

    private SequencePosition DecodeFromSpan(ReadOnlyMemory<byte> memory, in ReadOnlySequence<byte> sequence)
    {
        var span = memory.Span;
        var offset = 0;

        while (offset < span.Length)
        {
            var slice = span[offset..];
            var sliceMemory = memory[offset..];
            var result = TryDecodeFrame(slice, sliceMemory, out var frame, out var frameConsumed);

            if (result == DecodeStatus.NeedMoreData)
            {
                break;
            }

            offset += frameConsumed;

            if (frame is not null)
            {
                _frames.Add(frame);
            }
        }

        return sequence.GetPosition(offset);
    }

    private SequencePosition DecodeFromSequence(in ReadOnlySequence<byte> input)
    {
        var reader = new SequenceReader<byte>(input);

        while (reader.Remaining > 0)
        {
            var checkpoint = reader.Position;

            if (!TryDecodeVarInt(ref reader, out var rawType, out var typeBytes))
            {
                return checkpoint;
            }

            if (!TryDecodeVarInt(ref reader, out var payloadLength, out var lengthBytes))
            {
                return checkpoint;
            }

            if (payloadLength > int.MaxValue)
            {
                throw new HttpProtocolException(
                    $"HTTP/3 frame payload length {payloadLength} exceeds maximum decodable size.");
            }

            var payloadLen = (int)payloadLength;

            if (reader.Remaining < payloadLen)
            {
                return checkpoint;
            }

            ReadOnlyMemory<byte> payloadMemory;
            var payloadSeq = input.Slice(reader.Position, payloadLen);
            if (payloadSeq.IsSingleSegment)
            {
                payloadMemory = payloadSeq.First;
            }
            else
            {
                var buf = new byte[payloadLen];
                payloadSeq.CopyTo(buf);
                payloadMemory = buf;
            }

            reader.Advance(payloadLen);

            if (Enum.IsDefined((FrameType)rawType))
            {
                var frame = (FrameType)rawType switch
                {
                    FrameType.Data => (Http3Frame)new DataFrame(payloadMemory),
                    FrameType.Headers => new HeadersFrame(payloadMemory),
                    FrameType.CancelPush => DecodeCancelPushFrame(payloadMemory.Span),
                    FrameType.Settings => DecodeSettingsFrame(payloadMemory.Span),
                    FrameType.PushPromise => DecodePushPromiseFrame(payloadMemory),
                    FrameType.GoAway => DecodeGoAwayFrame(payloadMemory.Span),
                    FrameType.MaxPushId => DecodeMaxPushIdFrame(payloadMemory.Span),
                    _ => null
                };

                if (frame is not null)
                {
                    _frames.Add(frame);
                }
            }
        }

        return reader.Position;
    }

    private static bool TryDecodeVarInt(ref SequenceReader<byte> reader, out long value, out int bytesRead)
    {
        value = 0;
        bytesRead = 0;

        if (!reader.TryPeek(out var firstByte))
        {
            return false;
        }

        var length = 1 << (firstByte >> 6);

        if (reader.Remaining < length)
        {
            return false;
        }

        Span<byte> buf = stackalloc byte[8];
        buf.Clear();

        for (var i = 0; i < length; i++)
        {
            reader.TryRead(out buf[i]);
        }

        buf[0] &= 0x3F;

        value = length switch
        {
            1 => buf[0],
            2 => (buf[0] << 8) | buf[1],
            4 => (buf[0] << 24) | (buf[1] << 16) | (buf[2] << 8) | buf[3],
            8 => ((long)buf[0] << 56) | ((long)buf[1] << 48) | ((long)buf[2] << 40) | ((long)buf[3] << 32)
                 | ((long)buf[4] << 24) | ((long)buf[5] << 16) | ((long)buf[6] << 8) | buf[7],
            _ => 0
        };

        bytesRead = length;
        return true;
    }

    protected override void OnReset()
    {
    }

    public bool HasRemainder => false;

    private static DecodeStatus TryDecodeFrame(
        ReadOnlySpan<byte> data,
        ReadOnlyMemory<byte> dataMemory,
        out Http3Frame? frame,
        out int totalConsumed)
    {
        frame = null;
        totalConsumed = 0;

        if (!QuicVarInt.TryDecode(data, out var rawType, out var typeBytes))
        {
            return DecodeStatus.NeedMoreData;
        }

        if (!QuicVarInt.TryDecode(data[typeBytes..], out var payloadLength, out var lengthBytes))
        {
            return DecodeStatus.NeedMoreData;
        }

        var headerSize = typeBytes + lengthBytes;

        if (payloadLength > int.MaxValue - headerSize)
        {
            throw new HttpProtocolException(
                $"HTTP/3 frame payload length {payloadLength} exceeds maximum decodable size.");
        }

        var frameSize = headerSize + (int)payloadLength;

        if (data.Length < frameSize)
        {
            return DecodeStatus.NeedMoreData;
        }

        var payloadMemory = dataMemory.Slice(headerSize, (int)payloadLength);
        totalConsumed = frameSize;

        if (!Enum.IsDefined((FrameType)rawType))
        {
            frame = null;
            return DecodeStatus.Success;
        }

        frame = (FrameType)rawType switch
        {
            FrameType.Data => new DataFrame(payloadMemory),
            FrameType.Headers => new HeadersFrame(payloadMemory),
            FrameType.CancelPush => DecodeCancelPushFrame(payloadMemory.Span),
            FrameType.Settings => DecodeSettingsFrame(payloadMemory.Span),
            FrameType.PushPromise => DecodePushPromiseFrame(payloadMemory),
            FrameType.GoAway => DecodeGoAwayFrame(payloadMemory.Span),
            FrameType.MaxPushId => DecodeMaxPushIdFrame(payloadMemory.Span),
            _ => null
        };

        return DecodeStatus.Success;
    }

    private static long DecodeVarIntOrThrow(ReadOnlySpan<byte> span, out int bytesRead, string frameName)
    {
        if (!QuicVarInt.TryDecode(span, out var value, out bytesRead))
        {
            throw new HttpProtocolException(
                string.Concat("HTTP/3 ", frameName, " frame payload truncated (RFC 9114 §7.1)."));
        }

        return value;
    }

    private static CancelPushFrame DecodeCancelPushFrame(ReadOnlySpan<byte> payload)
    {
        var pushId = DecodeVarIntOrThrow(payload, out _, "CANCEL_PUSH");
        return new CancelPushFrame(pushId);
    }

    private static SettingsFrame DecodeSettingsFrame(ReadOnlySpan<byte> payload)
    {
        var parameters = new List<(long Identifier, long Value)>();
        var offset = 0;

        while (offset < payload.Length)
        {
            var id = DecodeVarIntOrThrow(payload[offset..], out var idBytes, "SETTINGS");
            offset += idBytes;

            var value = DecodeVarIntOrThrow(payload[offset..], out var valBytes, "SETTINGS");
            offset += valBytes;

            parameters.Add((id, value));
        }

        return new SettingsFrame(parameters);
    }

    private static PushPromiseFrame DecodePushPromiseFrame(ReadOnlyMemory<byte> payloadMemory)
    {
        var pushId = DecodeVarIntOrThrow(payloadMemory.Span, out var pushIdBytes, "PUSH_PROMISE");
        return new PushPromiseFrame(pushId, payloadMemory[pushIdBytes..]);
    }

    private static GoAwayFrame DecodeGoAwayFrame(ReadOnlySpan<byte> payload)
    {
        var streamId = DecodeVarIntOrThrow(payload, out _, "GOAWAY");
        return new GoAwayFrame(streamId);
    }

    private static MaxPushIdFrame DecodeMaxPushIdFrame(ReadOnlySpan<byte> payload)
    {
        var pushId = DecodeVarIntOrThrow(payload, out _, "MAX_PUSH_ID");
        return new MaxPushIdFrame(pushId);
    }
}
