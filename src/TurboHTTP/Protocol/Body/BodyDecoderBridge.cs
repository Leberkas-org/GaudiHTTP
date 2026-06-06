using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TurboHTTP.Protocol.Body;

internal readonly struct BodyBridgeFeedResult(int rawConsumed, bool isComplete)
{
    public int RawConsumed { get; } = rawConsumed;
    public bool IsComplete { get; } = isComplete;
}

internal sealed class BodyDecoderBridge(IFramingDecoder framing, BridgedBodyReader reader)
{
    public BodyBridgeFeedResult FeedStreamed(ReadOnlyMemory<byte> input, Action onConsumed)
    {
        var result = framing.Decode(input.Span, out var rawConsumed);

        if (!result.Body.IsEmpty)
        {
            var bodyMemory = framing.SupportsZeroCopy
                ? SliceFromInput(input, result.Body)
                : CopyToPooled(result.Body);

            if (result.EndOfBody)
            {
                reader.Supply(bodyMemory, () =>
                {
                    if (!framing.SupportsZeroCopy)
                    {
                        ReturnPooled(bodyMemory);
                    }

                    onConsumed();
                    reader.Complete();
                });
            }
            else
            {
                reader.Supply(bodyMemory, () =>
                {
                    if (!framing.SupportsZeroCopy)
                    {
                        ReturnPooled(bodyMemory);
                    }

                    onConsumed();
                });
            }
        }
        else if (result.EndOfBody)
        {
            reader.Complete();
        }

        return new BodyBridgeFeedResult(rawConsumed, result.EndOfBody);
    }

    public bool SignalEof()
    {
        var ok = framing.OnEof();
        if (ok)
        {
            reader.Complete();
        }

        return ok;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ReadOnlyMemory<byte> SliceFromInput(ReadOnlyMemory<byte> input, ReadOnlySpan<byte> body)
    {
        ref var inputStart = ref MemoryMarshal.GetReference(input.Span);
        ref var bodyStart = ref MemoryMarshal.GetReference(body);
        var offset = (int)Unsafe.ByteOffset(ref inputStart, ref bodyStart);
        return input.Slice(offset, body.Length);
    }

    private static ReadOnlyMemory<byte> CopyToPooled(ReadOnlySpan<byte> body)
    {
        var rental = ArrayPool<byte>.Shared.Rent(body.Length);
        body.CopyTo(rental);
        return rental.AsMemory(0, body.Length);
    }

    private static void ReturnPooled(ReadOnlyMemory<byte> memory)
    {
        if (MemoryMarshal.TryGetArray(memory, out var segment) && segment.Array is not null)
        {
            ArrayPool<byte>.Shared.Return(segment.Array);
        }
    }
}
