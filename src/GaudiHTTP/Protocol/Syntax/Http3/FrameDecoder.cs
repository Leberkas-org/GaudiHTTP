using System.Buffers;

namespace GaudiHTTP.Protocol.Syntax.Http3;

/// <summary>
/// Stateful HTTP/3 frame decoder per RFC 9114 §7.
/// Handles partial frames across QUIC stream boundaries by buffering
/// incomplete data between calls to <see cref="TryDecode"/>.
/// Unknown frame types are skipped gracefully per RFC 9114 §7.2.8.
///
/// Remainder bytes and combined working buffers are rented from <see cref="MemoryPool{T}"/>
/// to eliminate per-frame GC allocations. Frame payloads use <see cref="MemoryPool{T}"/>
/// and are returned via <see cref="IDisposable"/> on the frame objects.
/// Call <see cref="Dispose"/> when the decoder is no longer needed.
/// </summary>
internal sealed class FrameDecoder : IDisposable
{
    // MemoryPool-rented buffer holding the partial frame from the previous call.
    // _remainderOwner is null when not rented; _remainderLength tracks actual content.
    private IMemoryOwner<byte>? _remainderOwner;
    private int _remainderLength;

    // Reused per-DecodeAll-call frame list. Cleared at the start of each call.
    // Safe to reuse: Akka back-pressure guarantees all frames are consumed by downstream
    // before the next DecodeAll call.
    private readonly List<Http3Frame> _frames = [];

    /// <summary>
    /// Attempts to decode one HTTP/3 frame from <paramref name="input"/>.
    /// On <see cref="DecodeStatus.Success"/>, <paramref name="frame"/> is set and
    /// <paramref name="bytesConsumed"/> reflects the total bytes consumed from the
    /// combined remainder + input buffer.
    /// On <see cref="DecodeStatus.NeedMoreData"/>, the unconsumed data is buffered
    /// internally for the next call.
    /// </summary>
    public DecodeStatus TryDecode(ReadOnlySpan<byte> input, out Http3Frame? frame, out int bytesConsumed)
        => TryDecodeCore(input, default, sliceInput: false, out frame, out bytesConsumed);

    private DecodeStatus TryDecodeCore(ReadOnlySpan<byte> input, ReadOnlyMemory<byte> inputMemory, bool sliceInput,
        out Http3Frame? frame, out int bytesConsumed)
    {
        frame = null;
        bytesConsumed = 0;

        // Combine remainder with new input into a pooled working buffer
        ReadOnlySpan<byte> data;
        ReadOnlyMemory<byte> dataMemory = default;
        IMemoryOwner<byte>? rentedCombined = null;
        var combinedLength = 0;

        if (_remainderLength > 0)
        {
            combinedLength = _remainderLength + input.Length;
            rentedCombined = MemoryPool<byte>.Shared.Rent(combinedLength);
            _remainderOwner!.Memory.Span[.._remainderLength].CopyTo(rentedCombined.Memory.Span);
            input.CopyTo(rentedCombined.Memory.Span[_remainderLength..]);
            data = rentedCombined.Memory.Span[..combinedLength];
            sliceInput = false;

            // Dispose old remainder buffer now that its content has been copied out
            _remainderOwner?.Dispose();
            _remainderOwner = null;
            _remainderLength = 0;
        }
        else
        {
            data = input;
            dataMemory = inputMemory;
        }

        try
        {
            var result = TryDecodeFrame(data, dataMemory, sliceInput, out frame, out var totalConsumed);

            if (result == DecodeStatus.NeedMoreData)
            {
                // Buffer unconsumed data for next call
                if (data.Length > 0)
                {
                    _remainderOwner = MemoryPool<byte>.Shared.Rent(data.Length);
                    data.CopyTo(_remainderOwner.Memory.Span);
                    _remainderLength = data.Length;
                }

                bytesConsumed = input.Length; // All input consumed (buffered)
                return DecodeStatus.NeedMoreData;
            }

            // Calculate how many bytes of the original input were consumed
            if (rentedCombined != null)
            {
                // All input bytes are accounted for: some went into the decoded frame
                // (together with the old remainder), the rest is buffered as the new remainder.
                // Returning input.Length prevents DecodeAll from re-passing bytes that are
                // already captured in the remainder - avoiding double-counting corruption.
                bytesConsumed = input.Length;

                // Buffer any leftover from combined
                var leftover = combinedLength - totalConsumed;
                if (leftover > 0)
                {
                    _remainderOwner = MemoryPool<byte>.Shared.Rent(leftover);
                    rentedCombined.Memory.Span.Slice(totalConsumed, leftover).CopyTo(_remainderOwner.Memory.Span);
                    _remainderLength = leftover;
                }
            }
            else
            {
                bytesConsumed = totalConsumed;
            }

            return DecodeStatus.Success;
        }
        finally
        {
            rentedCombined?.Dispose();
        }
    }

    /// <summary>
    /// Attempts to decode all available frames from <paramref name="input"/>.
    /// Returns the list of decoded frames and the total bytes consumed from the input.
    /// Any trailing partial frame is buffered for the next call.
    /// </summary>
    public IReadOnlyList<Http3Frame> DecodeAll(ReadOnlySpan<byte> input, out int bytesConsumed)
        => DecodeAllCore(input, default, sliceInput: false, out bytesConsumed);

    /// <summary>
    /// Zero-copy variant: DATA/HEADERS/PUSH_PROMISE payloads of frames fully contained in
    /// <paramref name="input"/> are returned as slices of it — the caller must keep the
    /// backing buffer alive until all returned frames have been handled. Frames assembled
    /// from a buffered remainder still own a pooled copy.
    /// </summary>
    public IReadOnlyList<Http3Frame> DecodeAll(ReadOnlyMemory<byte> input, out int bytesConsumed)
        => DecodeAllCore(input.Span, input, sliceInput: true, out bytesConsumed);

    private IReadOnlyList<Http3Frame> DecodeAllCore(ReadOnlySpan<byte> input, ReadOnlyMemory<byte> inputMemory,
        bool sliceInput, out int bytesConsumed)
    {
        _frames.Clear();
        bytesConsumed = 0;

        while (true)
        {
            var remainingMemory = sliceInput ? inputMemory[bytesConsumed..] : default;
            var status = TryDecodeCore(input[bytesConsumed..], remainingMemory, sliceInput,
                out var frame, out var consumed);

            if (status == DecodeStatus.NeedMoreData)
            {
                break;
            }

            bytesConsumed += consumed;

            // Skip null frames (unknown frame types silently ignored per RFC 9114 §7.2.8)
            if (frame is not null)
            {
                _frames.Add(frame);
            }
        }

        return _frames;
    }

    /// <summary>
    /// Resets the decoder state, discarding any buffered partial frame data.
    /// </summary>
    public void Reset()
    {
        _remainderOwner?.Dispose();
        _remainderOwner = null;
        _remainderLength = 0;
    }

    /// <summary>
    /// Disposes the decoder, returning any pooled remainder buffer to the pool.
    /// </summary>
    public void Dispose() => Reset();

    /// <summary>
    /// Returns <c>true</c> if the decoder has buffered partial frame data.
    /// </summary>
    public bool HasRemainder => _remainderLength > 0;

    private static DecodeStatus TryDecodeFrame(
        ReadOnlySpan<byte> data,
        ReadOnlyMemory<byte> dataMemory,
        bool sliceInput,
        out Http3Frame? frame,
        out int totalConsumed)
    {
        frame = null;
        totalConsumed = 0;

        // Decode frame type (QUIC varint)
        if (!QuicVarInt.TryDecode(data, out var rawType, out var typeBytes))
        {
            return DecodeStatus.NeedMoreData;
        }

        // Decode frame length (QUIC varint)
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

        // Need more data for the payload
        if (data.Length < frameSize)
        {
            return DecodeStatus.NeedMoreData;
        }

        var payload = data.Slice(headerSize, (int)payloadLength);
        var payloadMemory = sliceInput ? dataMemory.Slice(headerSize, (int)payloadLength) : default;
        totalConsumed = frameSize;

        // Parse frame by type
        if (!Enum.IsDefined((FrameType)rawType))
        {
            // Unknown frame type - skip gracefully per RFC 9114 §7.2.8
            // Return a success with null frame to indicate skipped unknown frame
            frame = null;

            // We still consumed the bytes, but we need to signal this differently.
            // Use a sentinel: return Success but with frame = null means "skipped unknown type".
            // The caller can check frame == null to detect this.
            return DecodeStatus.Success;
        }

        var frameType = (FrameType)rawType;

        frame = frameType switch
        {
            FrameType.Data => DecodeDataFrame(payload, payloadMemory, sliceInput),
            FrameType.Headers => DecodeHeadersFrame(payload, payloadMemory, sliceInput),
            FrameType.CancelPush => DecodeCancelPushFrame(payload),
            FrameType.Settings => DecodeSettingsFrame(payload),
            FrameType.PushPromise => DecodePushPromiseFrame(payload, payloadMemory, sliceInput),
            FrameType.GoAway => DecodeGoAwayFrame(payload),
            FrameType.MaxPushId => DecodeMaxPushIdFrame(payload),
            _ => null // Should not happen given IsDefined check above
        };

        return DecodeStatus.Success;
    }

    private static DataFrame DecodeDataFrame(ReadOnlySpan<byte> payload, ReadOnlyMemory<byte> payloadMemory,
        bool sliceInput)
    {
        if (payload.Length == 0)
        {
            return new DataFrame(ReadOnlyMemory<byte>.Empty);
        }

        if (sliceInput)
        {
            return new DataFrame(payloadMemory);
        }

        var owner = MemoryPool<byte>.Shared.Rent(payload.Length);
        payload.CopyTo(owner.Memory.Span);
        return new DataFrame(owner, payload.Length);
    }

    private static HeadersFrame DecodeHeadersFrame(ReadOnlySpan<byte> payload, ReadOnlyMemory<byte> payloadMemory,
        bool sliceInput)
    {
        if (payload.Length == 0)
        {
            return new HeadersFrame(ReadOnlyMemory<byte>.Empty);
        }

        if (sliceInput)
        {
            return new HeadersFrame(payloadMemory);
        }

        var owner = MemoryPool<byte>.Shared.Rent(payload.Length);
        payload.CopyTo(owner.Memory.Span);
        return new HeadersFrame(owner, payload.Length);
    }

    /// <summary>
    /// Decodes a QUIC varint from a frame body, translating a too-short payload into a clean
    /// <see cref="HttpProtocolException"/> (RFC 9114 §7.1 frame error) instead of the raw
    /// <see cref="ArgumentException"/> that <see cref="QuicVarInt.Decode"/> throws — the latter
    /// escapes the protocol-error catch filters and is silently swallowed by the stage.
    /// </summary>
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

    private static PushPromiseFrame DecodePushPromiseFrame(ReadOnlySpan<byte> payload,
        ReadOnlyMemory<byte> payloadMemory, bool sliceInput)
    {
        var pushId = DecodeVarIntOrThrow(payload, out var pushIdBytes, "PUSH_PROMISE");
        var headerBlockSpan = payload[pushIdBytes..];

        if (headerBlockSpan.Length == 0)
        {
            return new PushPromiseFrame(pushId, ReadOnlyMemory<byte>.Empty);
        }

        if (sliceInput)
        {
            return new PushPromiseFrame(pushId, payloadMemory[pushIdBytes..]);
        }

        var owner = MemoryPool<byte>.Shared.Rent(headerBlockSpan.Length);
        headerBlockSpan.CopyTo(owner.Memory.Span);
        return new PushPromiseFrame(pushId, owner, headerBlockSpan.Length);
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