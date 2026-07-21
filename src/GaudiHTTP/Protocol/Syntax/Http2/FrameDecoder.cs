using System.Buffers;
using System.Buffers.Binary;

namespace GaudiHTTP.Protocol.Syntax.Http2;

/// <summary>
/// Stateful HTTP/2 frame decoder per RFC 9113 §4.1.
/// Accepts <see cref="ReadOnlySequence{T}"/> and returns a <see cref="SequencePosition"/>
/// indicating how far into the input frames were fully decoded. Unconsumed trailing
/// bytes are NOT buffered internally — the caller (or Pipe) retains them via AdvanceTo.
/// The returned list is reused on every call — callers MUST fully consume it
/// before the next DecodeAll and MUST NOT retain it.
/// </summary>
internal sealed class FrameDecoder(int maxFrameSize = (int)FrameDecoder.MaxMaxFrameSize) : IDisposable
{
    private const int FrameHeaderSize = 9;
    private const uint StreamIdMask = 0x7FFFFFFFu;
    private const int PriorityFieldSize = 5;
    private const int RstStreamPayloadSize = 4;
    private const int SettingsEntrySize = 6;
    private const int SettingsValueOffset = 2;
    private const uint MinMaxFrameSize = 16 * 1024;
    private const uint MaxMaxFrameSize = 16 * 1024 * 1024 - 1;
    private const int PingPayloadSize = 8;
    private const int GoAwayMinPayloadSize = 8;
    private const int GoAwayErrorCodeOffset = 4;
    private const int PushPromiseHeaderBlockOffset = 4;
    private const int WindowUpdatePayloadSize = 4;
    private const int PadLengthFieldSize = 1;

    private readonly List<Http2Frame> _frames = new(10);

    private int _awaitingContinuationStreamId;

    public IReadOnlyList<Http2Frame> DecodeAll(in ReadOnlySequence<byte> input, out SequencePosition consumed)
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

        while (span.Length - offset >= FrameHeaderSize)
        {
            var header = span[offset..];
            var payloadLen = (header[0] << 16) | (header[1] << 8) | header[2];

            if (payloadLen > maxFrameSize)
            {
                ThrowOversizedFrame(payloadLen);
            }

            if (span.Length - offset < FrameHeaderSize + payloadLen)
            {
                break;
            }

            var type = (FrameType)header[3];
            var flags = header[4];
            var streamId = (int)(BinaryPrimitives.ReadUInt32BigEndian(header[5..]) & StreamIdMask);
            var payload = memory.Slice(offset + FrameHeaderSize, payloadLen);

            var frame = CreateFrame(type, flags, streamId, payload);
            if (frame != null)
            {
                ValidateContinuationState(type, streamId);
                UpdateContinuationState(frame);
                _frames.Add(frame);
            }

            offset += FrameHeaderSize + payloadLen;
        }

        return sequence.GetPosition(offset);
    }

    private SequencePosition DecodeFromSequence(in ReadOnlySequence<byte> input)
    {
        var reader = new SequenceReader<byte>(input);

        while (reader.Remaining >= FrameHeaderSize)
        {
            var checkpoint = reader.Position;

            Span<byte> headerBuf = stackalloc byte[FrameHeaderSize];
            if (!reader.TryCopyTo(headerBuf))
            {
                reader.Rewind(reader.Consumed - input.GetOffset(checkpoint));
                break;
            }

            var payloadLen = (headerBuf[0] << 16) | (headerBuf[1] << 8) | headerBuf[2];

            if (payloadLen > maxFrameSize)
            {
                ThrowOversizedFrame(payloadLen);
            }

            if (reader.Remaining - FrameHeaderSize < payloadLen)
            {
                reader = new SequenceReader<byte>(input.Slice(checkpoint));
                return checkpoint;
            }

            reader.Advance(FrameHeaderSize);

            var type = (FrameType)headerBuf[3];
            var flags = headerBuf[4];
            var streamId = (int)(BinaryPrimitives.ReadUInt32BigEndian(headerBuf[5..]) & StreamIdMask);

            var payloadSeq = input.Slice(reader.Position, payloadLen);
            ReadOnlyMemory<byte> payload;
            if (payloadSeq.IsSingleSegment)
            {
                payload = payloadSeq.First;
            }
            else
            {
                var buf = new byte[payloadLen];
                payloadSeq.CopyTo(buf);
                payload = buf;
            }

            reader.Advance(payloadLen);

            var frame = CreateFrame(type, flags, streamId, payload);
            if (frame != null)
            {
                ValidateContinuationState(type, streamId);
                UpdateContinuationState(frame);
                _frames.Add(frame);
            }
        }

        return reader.Position;
    }

    private void ThrowOversizedFrame(int payloadLen)
    {
        throw new HttpProtocolException(
            $"RFC 9113 §4.2: frame payload length {payloadLen} exceeds advertised SETTINGS_MAX_FRAME_SIZE {maxFrameSize}.");
    }

    public void Reset()
    {
        _awaitingContinuationStreamId = 0;
    }

    public void Dispose()
    {
    }

    private static Http2Frame? CreateFrame(FrameType type, byte flags, int streamId, ReadOnlyMemory<byte> payload)
    {
        return type switch
        {
            FrameType.Data => ParseDataFrame(flags, streamId, payload),
            FrameType.Headers => ParseHeadersFrame(flags, streamId, payload),
            FrameType.Continuation => streamId == 0
                ? throw new HttpProtocolException(
                    "RFC 9113 §6.10: CONTINUATION frame MUST be associated with a stream; stream 0 is invalid.")
                : new ContinuationFrame(
                    streamId,
                    payload,
                    (flags & (byte)Continuations.EndHeaders) != 0),
            FrameType.Ping => streamId != 0
                ? throw new HttpProtocolException("RFC 9113 §6.7: PING frame MUST be sent on stream 0.")
                : CreatePing(flags, payload),
            FrameType.Settings => streamId != 0
                ? throw new HttpProtocolException("RFC 9113 §6.5: SETTINGS frame MUST be sent on stream 0.")
                : ParseSettings(payload, flags),
            FrameType.WindowUpdate => CreateWindowUpdateFrame(streamId, payload),
            FrameType.RstStream => payload.Length == RstStreamPayloadSize
                ? new RstStreamFrame(streamId, (Http2ErrorCode)BinaryPrimitives.ReadUInt32BigEndian(payload.Span))
                : throw new HttpProtocolException(
                    $"RFC 9113 §6.4: RST_STREAM frame must be exactly {RstStreamPayloadSize} bytes; got {payload.Length}."),
            FrameType.GoAway => streamId != 0
                ? throw new HttpProtocolException(
                    "RFC 9113 §6.8: GOAWAY frame MUST be sent on stream 0.")
                : ParseGoAway(payload),
            FrameType.PushPromise => ParsePushPromise(streamId, flags, payload),
            _ => null
        };
    }

    private static DataFrame ParseDataFrame(byte flags, int streamId, ReadOnlyMemory<byte> payload)
    {
        if (streamId == 0)
        {
            throw new HttpProtocolException(
                "RFC 9113 §6.1: DATA frame MUST be associated with a stream; stream 0 is invalid.");
        }

        var endStream = (flags & (byte)Datas.EndStream) != 0;
        var data = payload;
        var flowControlledLength = payload.Length;

        if ((flags & (byte)Datas.Padded) != 0)
        {
            if (data.IsEmpty)
            {
                throw new HttpProtocolException("DATA PADDED frame: payload is empty");
            }

            var padLen = data.Span[0];
            if (PadLengthFieldSize + padLen > data.Length)
            {
                throw new HttpProtocolException("DATA PADDED frame: pad_length exceeds payload size");
            }

            data = data.Slice(PadLengthFieldSize, data.Length - PadLengthFieldSize - padLen);
        }

        return new DataFrame(streamId, data, endStream, flowControlledLength);
    }

    private static HeadersFrame ParseHeadersFrame(byte flags, int streamId, ReadOnlyMemory<byte> payload)
    {
        var endStream = (flags & (byte)Headers.EndStream) != 0;
        var endHeaders = (flags & (byte)Headers.EndHeaders) != 0;
        var data = payload;

        if ((flags & (byte)Headers.Padded) != 0)
        {
            if (data.IsEmpty)
            {
                throw new HttpProtocolException("HEADERS PADDED frame: payload is empty");
            }

            var padLen = data.Span[0];
            if (PadLengthFieldSize + padLen > data.Length)
            {
                throw new HttpProtocolException("HEADERS PADDED frame: pad_length exceeds payload size");
            }

            data = data.Slice(PadLengthFieldSize, data.Length - PadLengthFieldSize - padLen);
        }

        if ((flags & (byte)Headers.Priority) != 0)
        {
            data = data.Length >= PriorityFieldSize ? data[PriorityFieldSize..] : ReadOnlyMemory<byte>.Empty;
        }

        return new HeadersFrame(streamId, data, endStream, endHeaders);
    }

    private static PingFrame CreatePing(byte flags, ReadOnlyMemory<byte> payload)
    {
        if (payload.Length != PingPayloadSize)
        {
            throw new HttpProtocolException(
                $"PING frame must be exactly {PingPayloadSize} bytes, got {payload.Length}");
        }

        return new PingFrame(payload, (flags & (byte)Pings.Ack) != 0);
    }

    private static SettingsFrame ParseSettings(ReadOnlyMemory<byte> payload, byte flags)
    {
        var isAck = (flags & (byte)Settings.Ack) != 0;

        if (isAck && payload.Length > 0)
        {
            throw new HttpProtocolException(
                "RFC 9113 §6.5: SETTINGS frame with ACK flag MUST have empty payload.");
        }

        if (!isAck && payload.Length % SettingsEntrySize != 0)
        {
            throw new HttpProtocolException(
                $"RFC 9113 §6.5: SETTINGS payload length {payload.Length} is not a multiple of {SettingsEntrySize}.");
        }

        var entryCount = payload.Length / SettingsEntrySize;
        var span = payload.Span;
        var list = new List<(SettingsParameter, uint)>(entryCount);

        for (var i = 0; i + SettingsEntrySize <= span.Length; i += SettingsEntrySize)
        {
            var key = (SettingsParameter)BinaryPrimitives.ReadUInt16BigEndian(span[i..]);
            var value = BinaryPrimitives.ReadUInt32BigEndian(span[(i + SettingsValueOffset)..]);

            if (key == SettingsParameter.MaxFrameSize && value is < MinMaxFrameSize or > MaxMaxFrameSize)
            {
                throw new HttpProtocolException(
                    $"RFC 9113 §6.5.2: SETTINGS_MAX_FRAME_SIZE {value} is outside the valid range [{MinMaxFrameSize}, {MaxMaxFrameSize}].");
            }

            if (key == SettingsParameter.InitialWindowSize && value > int.MaxValue)
            {
                throw new HttpProtocolException(
                    $"RFC 9113 §6.5.2: SETTINGS_INITIAL_WINDOW_SIZE {value} exceeds the maximum 2^31-1 (FLOW_CONTROL_ERROR).");
            }

            list.Add((key, value));
        }

        return new SettingsFrame(list, isAck);
    }

    private static GoAwayFrame ParseGoAway(ReadOnlyMemory<byte> payload)
    {
        if (payload.Length < GoAwayMinPayloadSize)
        {
            throw new HttpProtocolException(
                $"RFC 9113 §6.8: GOAWAY payload must be at least {GoAwayMinPayloadSize} bytes; got {payload.Length}.");
        }

        var span = payload.Span;
        var lastStream = (int)(BinaryPrimitives.ReadUInt32BigEndian(span) & StreamIdMask);
        var errorCode = (Http2ErrorCode)BinaryPrimitives.ReadUInt32BigEndian(span[GoAwayErrorCodeOffset..]);
        var debugData = span.Length > GoAwayMinPayloadSize
            ? payload[GoAwayMinPayloadSize..]
            : ReadOnlyMemory<byte>.Empty;
        return new GoAwayFrame(lastStream, errorCode, debugData);
    }

    private static PushPromiseFrame ParsePushPromise(int streamId, byte flags, ReadOnlyMemory<byte> payload)
    {
        if (payload.Length < PushPromiseHeaderBlockOffset)
        {
            throw new HttpProtocolException(
                $"RFC 9113 §6.6: PUSH_PROMISE payload must be at least {PushPromiseHeaderBlockOffset} bytes; got {payload.Length}.");
        }

        var span = payload.Span;
        var promised = (int)(BinaryPrimitives.ReadUInt32BigEndian(span) & StreamIdMask);
        var endHeaders = (flags & (byte)Headers.EndHeaders) != 0;
        return new PushPromiseFrame(streamId, promised, payload[PushPromiseHeaderBlockOffset..], endHeaders);
    }

    private static WindowUpdateFrame CreateWindowUpdateFrame(int streamId, ReadOnlyMemory<byte> payload)
    {
        if (payload.Length != WindowUpdatePayloadSize)
        {
            throw new HttpProtocolException(
                $"RFC 9113 §6.9: WINDOW_UPDATE payload must be exactly {WindowUpdatePayloadSize} bytes; got {payload.Length}.");
        }

        var increment = (int)(BinaryPrimitives.ReadUInt32BigEndian(payload.Span) & StreamIdMask);
        if (increment == 0)
        {
            throw new HttpProtocolException(
                "RFC 9113 §6.9: WINDOW_UPDATE increment of 0 is a PROTOCOL_ERROR.");
        }

        return new WindowUpdateFrame(streamId, increment);
    }

    private void ValidateContinuationState(FrameType type, int streamId)
    {
        if (_awaitingContinuationStreamId != 0)
        {
            if (type != FrameType.Continuation)
            {
                throw new HttpProtocolException(
                    $"RFC 9113 §6.10: Expected CONTINUATION frame on stream {_awaitingContinuationStreamId}, but received {type}.");
            }

            if (streamId != _awaitingContinuationStreamId)
            {
                throw new HttpProtocolException(
                    $"RFC 9113 §6.10: Expected CONTINUATION on stream {_awaitingContinuationStreamId}, but received on stream {streamId}.");
            }
        }
        else if (type == FrameType.Continuation)
        {
            throw new HttpProtocolException(
                "RFC 9113 §6.10: CONTINUATION frame received without preceding HEADERS or PUSH_PROMISE.");
        }
    }

    private void UpdateContinuationState(Http2Frame frame)
    {
        _awaitingContinuationStreamId = frame switch
        {
            HeadersFrame { EndHeaders: false } h => h.StreamId,
            HeadersFrame => 0,
            PushPromiseFrame { EndHeaders: false } pp => pp.StreamId,
            PushPromiseFrame or ContinuationFrame { EndHeaders: true } => 0,
            _ => _awaitingContinuationStreamId
        };
    }
}
