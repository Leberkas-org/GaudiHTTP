using GaudiHTTP.Pooling;

namespace GaudiHTTP.Protocol.Syntax.Http3;

/// <summary>
/// Stateful HTTP/3 frame decoder per RFC 9114 §7.
/// Handles partial frames across QUIC stream boundaries by buffering
/// incomplete data between calls to <see cref="DecodeAll"/>.
/// Unknown frame types are skipped gracefully per RFC 9114 §7.2.8.
///
/// Remainder bytes use a field-level byte[] (grow-on-demand, never-shrink)
/// to avoid per-frame MemoryPool allocations. Frame payloads from complete
/// frames in the input are zero-copy slices of the caller's buffer; payloads
/// assembled from a buffered remainder are slices of the remainder buffer
/// (valid until the next DecodeAll call).
/// </summary>
internal sealed class FrameDecoder : Poolable<FrameDecoder>
{
    private byte[]? _remainderBuffer;
    private int _remainderOffset;
    private int _remainderLength;

    private readonly List<Http3Frame> _frames = [];

    /// <summary>
    /// Decodes all available frames from <paramref name="input"/>.
    /// The returned list is reused on every call — callers MUST fully consume it
    /// before the next DecodeAll and MUST NOT retain it.
    /// </summary>
    public IReadOnlyList<Http3Frame> DecodeAll(ReadOnlyMemory<byte> input, out int bytesConsumed)
    {
        _frames.Clear();
        bytesConsumed = 0;

        if (_remainderLength > 0)
        {
            // Compact deferred remainder from previous call before any new frame
            // slices reference the buffer.
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

        while (offset < input.Length)
        {
            var slice = input[offset..];
            var result = TryDecodeFrame(slice.Span, slice, out var frame, out var consumed);

            if (result == DecodeStatus.NeedMoreData)
            {
                var leftover = input.Length - offset;
                _remainderOffset = 0;
                _remainderLength = 0;
                EnsureRemainderCapacity(leftover);
                input.Span[offset..].CopyTo(_remainderBuffer);
                _remainderLength = leftover;
                break;
            }

            offset += consumed;

            if (frame is not null)
            {
                _frames.Add(frame);
            }
        }
    }

    private void DecodeWithRemainder(ReadOnlyMemory<byte> input)
    {
        var needed = _remainderLength + input.Length;
        EnsureRemainderCapacity(needed);
        input.Span.CopyTo(_remainderBuffer.AsSpan(_remainderLength));
        _remainderLength = needed;

        while (_remainderLength > 0)
        {
            var dataMemory = _remainderBuffer.AsMemory(_remainderOffset, _remainderLength);
            var result = TryDecodeFrame(dataMemory.Span, dataMemory, out var frame, out var consumed);

            if (result == DecodeStatus.NeedMoreData)
            {
                break;
            }

            _remainderOffset += consumed;
            _remainderLength -= consumed;

            if (frame is not null)
            {
                _frames.Add(frame);
            }
        }
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

    protected override void OnReset()
    {
        _remainderBuffer = null;
        _remainderOffset = 0;
        _remainderLength = 0;
    }

    public bool HasRemainder => _remainderLength > 0;

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
