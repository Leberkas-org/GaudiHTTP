using System.Buffers.Binary;

namespace GaudiHTTP.Protocol.Syntax.Http2;

/// <summary>
/// Stateful HTTP/2 frame decoder per RFC 9113 §4.1.
/// Caller-owned buffer pattern: callers pass <see cref="ReadOnlyMemory{T}"/> and retain
/// buffer ownership. Frame payloads are zero-copy slices of the caller's memory when no
/// remainder is involved; frames assembled from a buffered remainder reference the
/// remainder buffer (valid until the next <see cref="DecodeAll"/> call).
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

    private byte[]? _remainderBuffer;
    private int _remainderOffset;
    private int _remainderLength;

    private readonly List<Http2Frame> _frames = new(10);

    private int _awaitingContinuationStreamId;

    public IReadOnlyList<Http2Frame> DecodeAll(ReadOnlyMemory<byte> input, out int bytesConsumed)
    {
        _frames.Clear();
        bytesConsumed = 0;

        if (input.Length == 0 && _remainderLength == 0)
        {
            return _frames;
        }

        if (_remainderLength > 0)
        {
            if (_remainderOffset > 0)
            {
                Buffer.BlockCopy(_remainderBuffer!, _remainderOffset, _remainderBuffer!, 0, _remainderLength);
                _remainderOffset = 0;
            }

            DecodeWithRemainder(input);
        }
        else
        {
            DecodeFromInput(input);
        }

        bytesConsumed = input.Length;
        return _frames;
    }

    private void DecodeFromInput(ReadOnlyMemory<byte> input)
    {
        var offset = 0;

        while (input.Length - offset >= FrameHeaderSize)
        {
            var span = input.Span[offset..];
            var payloadLen = (span[0] << 16) | (span[1] << 8) | span[2];

            if (payloadLen > maxFrameSize)
            {
                ThrowOversizedFrame(payloadLen, offset, input.Length, input.Span);
            }

            if (input.Length - offset < FrameHeaderSize + payloadLen)
            {
                break;
            }

            var type = (FrameType)span[3];
            var flags = span[4];
            var streamId = (int)(BinaryPrimitives.ReadUInt32BigEndian(span[5..]) & StreamIdMask);
            var payload = input.Slice(offset + FrameHeaderSize, payloadLen);

            var frame = CreateFrame(type, flags, streamId, payload);
            if (frame != null)
            {
                ValidateContinuationState(type, streamId);
                UpdateContinuationState(frame);
                _frames.Add(frame);
            }

            offset += FrameHeaderSize + payloadLen;
        }

        var leftover = input.Length - offset;
        if (leftover > 0)
        {
            _remainderOffset = 0;
            _remainderLength = 0;
            EnsureRemainderCapacity(leftover);
            input.Span[offset..].CopyTo(_remainderBuffer);
            _remainderLength = leftover;
        }
    }

    private void DecodeWithRemainder(ReadOnlyMemory<byte> input)
    {
        var needed = _remainderLength + input.Length;
        EnsureRemainderCapacity(needed);
        input.Span.CopyTo(_remainderBuffer.AsSpan(_remainderLength));
        _remainderLength = needed;

        while (_remainderLength - _remainderOffset >= FrameHeaderSize)
        {
            var span = _remainderBuffer.AsSpan(_remainderOffset, _remainderLength - _remainderOffset);
            var payloadLen = (span[0] << 16) | (span[1] << 8) | span[2];

            if (payloadLen > maxFrameSize)
            {
                ThrowOversizedFrame(payloadLen, _remainderOffset, _remainderLength, span);
            }

            var totalLength = _remainderLength - _remainderOffset;
            if (totalLength < FrameHeaderSize + payloadLen)
            {
                break;
            }

            var type = (FrameType)span[3];
            var flags = span[4];
            var streamId = (int)(BinaryPrimitives.ReadUInt32BigEndian(span[5..]) & StreamIdMask);
            var payload = _remainderBuffer.AsMemory(_remainderOffset + FrameHeaderSize, payloadLen);

            var frame = CreateFrame(type, flags, streamId, payload);
            if (frame != null)
            {
                ValidateContinuationState(type, streamId);
                UpdateContinuationState(frame);
                _frames.Add(frame);
            }

            _remainderOffset += FrameHeaderSize + payloadLen;
        }

        _remainderLength -= _remainderOffset;
    }

    private void EnsureRemainderCapacity(int needed)
    {
        if (_remainderBuffer is null || _remainderBuffer.Length < needed + _remainderOffset)
        {
            var newBuffer = new byte[Math.Max(needed, 256)];
            if (_remainderBuffer is not null && _remainderLength > 0)
            {
                _remainderBuffer.AsSpan(_remainderOffset, _remainderLength).CopyTo(newBuffer);
                _remainderOffset = 0;
            }

            _remainderBuffer = newBuffer;
        }
    }

    private void ThrowOversizedFrame(int payloadLen, int offset, int workingLength, ReadOnlySpan<byte> working)
    {
        var contextStart = Math.Max(0, offset - 16);
        var contextLength = Math.Min(48, workingLength - contextStart);
        var context = Convert.ToHexString(working.Slice(contextStart - (working.Length < workingLength ? 0 : offset - contextStart), Math.Min(contextLength, working.Length)));
        throw new HttpProtocolException(
            $"RFC 9113 §4.2: frame payload length {payloadLen} exceeds advertised SETTINGS_MAX_FRAME_SIZE {maxFrameSize}. "
            + $"Decoder state: offset={offset}, workingLength={workingLength}, remainderOffset={_remainderOffset}, "
            + $"remainderLength={_remainderLength}.");
    }

    public void Reset()
    {
        _remainderBuffer = null;
        _remainderOffset = 0;
        _remainderLength = 0;
        _awaitingContinuationStreamId = 0;
    }

    public void Dispose()
    {
        _remainderBuffer = null;
        _remainderOffset = 0;
        _remainderLength = 0;
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
