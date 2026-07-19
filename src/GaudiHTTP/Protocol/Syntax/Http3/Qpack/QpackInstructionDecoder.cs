using System.Buffers;

namespace GaudiHTTP.Protocol.Syntax.Http3.Qpack;

/// <summary>
/// RFC 9204 §4.3, §4.4 — Stateful decoder for QPACK instruction streams.
///
/// Parses encoder instructions (encoder→decoder stream):
///   - Set Dynamic Table Capacity (§4.3.1): 001xxxxx
///   - Insert With Name Reference (§4.3.2): 1Txxxxxx + value
///   - Insert With Literal Name (§4.3.3):   01Hxxxxx + name + value
///   - Duplicate (§4.3.4):                  000xxxxx
///
/// Parses decoder instructions (decoder→encoder stream):
///   - Section Acknowledgment (§4.4.1):     1xxxxxxx
///   - Stream Cancellation (§4.4.2):        01xxxxxx
///   - Insert Count Increment (§4.4.3):     00xxxxxx
///
/// Maintains a field-level byte[] remainder buffer (grow-on-demand, never-shrink)
/// for partial instructions split across reads, avoiding per-call MemoryPool allocations.
/// </summary>
internal sealed class QpackInstructionDecoder : IDisposable
{
    private byte[]? _remainderBuffer;
    private int _remainderLength;

    private byte[] _huffmanScratch = new byte[4 * 1024];

    private readonly HeaderNameCache _nameCache = new();

    public bool HasRemainder => _remainderLength > 0;

    public void Reset()
    {
        _remainderBuffer = null;
        _remainderLength = 0;
    }

    public void Dispose() => Reset();

    public QpackDecodeStatus TryDecodeEncoderInstruction(ReadOnlySpan<byte> data, out EncoderInstruction? instruction)
    {
        instruction = null;

        ReadOnlySpan<byte> span;
        int spanLength;

        if (_remainderLength == 0)
        {
            if (data.Length == 0)
            {
                return QpackDecodeStatus.NeedMoreData;
            }

            span = data;
            spanLength = data.Length;
        }
        else if (data.Length == 0)
        {
            span = _remainderBuffer.AsSpan(0, _remainderLength);
            spanLength = _remainderLength;
        }
        else
        {
            spanLength = _remainderLength + data.Length;
            EnsureRemainderCapacity(spanLength);
            data.CopyTo(_remainderBuffer.AsSpan(_remainderLength));
            span = _remainderBuffer.AsSpan(0, spanLength);
            _remainderLength = 0;
        }

        var pos = 0;

        try
        {
            var firstByte = span[0];

            if ((firstByte & 0x80) != 0)
            {
                var isStatic = (firstByte & 0x40) != 0;
                var nameIndex = QpackIntegerCodec.Decode(span, ref pos, 6);
                var value = QpackStringCodec.DecodeToString(span, ref pos, 7, ref _huffmanScratch);

                instruction = new EncoderInstruction
                {
                    Type = EncoderInstructionType.InsertWithNameReference,
                    NameIndex = nameIndex,
                    IsStatic = isStatic,
                    Value = value
                };
            }
            else if ((firstByte & 0x40) != 0)
            {
                var name = QpackStringCodec.DecodeToString(span, ref pos, 5, ref _huffmanScratch, _nameCache);
                var value = QpackStringCodec.DecodeToString(span, ref pos, 7, ref _huffmanScratch);

                instruction = new EncoderInstruction
                {
                    Type = EncoderInstructionType.InsertWithLiteralName,
                    Name = name,
                    Value = value
                };
            }
            else if ((firstByte & 0x20) != 0)
            {
                var capacity = QpackIntegerCodec.Decode(span, ref pos, 5);

                instruction = new EncoderInstruction
                {
                    Type = EncoderInstructionType.SetDynamicTableCapacity,
                    IntValue = capacity
                };
            }
            else
            {
                var index = QpackIntegerCodec.Decode(span, ref pos, 5);

                instruction = new EncoderInstruction
                {
                    Type = EncoderInstructionType.Duplicate,
                    IntValue = index
                };
            }

            if (pos < spanLength)
            {
                StoreRemainder(span[pos..]);
            }
            else
            {
                _remainderLength = 0;
            }

            return QpackDecodeStatus.Success;
        }
        catch (QpackException)
        {
            if (spanLength > 0)
            {
                StoreRemainder(span[..spanLength]);
            }

            return QpackDecodeStatus.NeedMoreData;
        }
    }

    public QpackDecodeStatus TryDecodeDecoderInstruction(ReadOnlySpan<byte> data, out DecoderInstruction? instruction)
    {
        instruction = null;

        ReadOnlySpan<byte> span;
        int spanLength;

        if (_remainderLength == 0)
        {
            if (data.Length == 0)
            {
                return QpackDecodeStatus.NeedMoreData;
            }

            span = data;
            spanLength = data.Length;
        }
        else if (data.Length == 0)
        {
            span = _remainderBuffer.AsSpan(0, _remainderLength);
            spanLength = _remainderLength;
        }
        else
        {
            spanLength = _remainderLength + data.Length;
            EnsureRemainderCapacity(spanLength);
            data.CopyTo(_remainderBuffer.AsSpan(_remainderLength));
            span = _remainderBuffer.AsSpan(0, spanLength);
            _remainderLength = 0;
        }

        var pos = 0;

        try
        {
            var firstByte = span[0];

            if ((firstByte & 0x80) != 0)
            {
                var streamId = QpackIntegerCodec.Decode(span, ref pos, 7);

                instruction = new DecoderInstruction
                {
                    Type = DecoderInstructionType.SectionAcknowledgment,
                    IntValue = streamId
                };
            }
            else if ((firstByte & 0x40) != 0)
            {
                var streamId = QpackIntegerCodec.Decode(span, ref pos, 6);

                instruction = new DecoderInstruction
                {
                    Type = DecoderInstructionType.StreamCancellation,
                    IntValue = streamId
                };
            }
            else
            {
                var increment = QpackIntegerCodec.Decode(span, ref pos, 6);

                instruction = new DecoderInstruction
                {
                    Type = DecoderInstructionType.InsertCountIncrement,
                    IntValue = increment
                };
            }

            if (pos < spanLength)
            {
                StoreRemainder(span[pos..]);
            }
            else
            {
                _remainderLength = 0;
            }

            return QpackDecodeStatus.Success;
        }
        catch (QpackException)
        {
            if (spanLength > 0)
            {
                StoreRemainder(span[..spanLength]);
            }

            return QpackDecodeStatus.NeedMoreData;
        }
    }

    public EncoderInstruction[] DecodeAllEncoderInstructions(ReadOnlySpan<byte> data)
    {
        var rented = ArrayPool<EncoderInstruction>.Shared.Rent(16);
        var count = 0;
        var first = true;

        while (true)
        {
            var input = first ? data : ReadOnlySpan<byte>.Empty;
            first = false;

            var status = TryDecodeEncoderInstruction(input, out var instruction);

            if (status == QpackDecodeStatus.NeedMoreData)
            {
                break;
            }

            if (count == rented.Length)
            {
                var larger = ArrayPool<EncoderInstruction>.Shared.Rent(rented.Length * 2);
                Array.Copy(rented, larger, count);
                ArrayPool<EncoderInstruction>.Shared.Return(rented, true);
                rented = larger;
            }

            rented[count++] = instruction!;
        }

        var result = new EncoderInstruction[count];
        Array.Copy(rented, result, count);
        ArrayPool<EncoderInstruction>.Shared.Return(rented, true);
        return result;
    }

    public DecoderInstruction[] DecodeAllDecoderInstructions(ReadOnlySpan<byte> data)
    {
        var rented = ArrayPool<DecoderInstruction>.Shared.Rent(16);
        var count = 0;
        var first = true;

        while (true)
        {
            var input = first ? data : ReadOnlySpan<byte>.Empty;
            first = false;

            var status = TryDecodeDecoderInstruction(input, out var instruction);

            if (status == QpackDecodeStatus.NeedMoreData)
            {
                break;
            }

            if (count == rented.Length)
            {
                var larger = ArrayPool<DecoderInstruction>.Shared.Rent(rented.Length * 2);
                Array.Copy(rented, larger, count);
                ArrayPool<DecoderInstruction>.Shared.Return(rented, true);
                rented = larger;
            }

            rented[count++] = instruction!;
        }

        var result = new DecoderInstruction[count];
        Array.Copy(rented, result, count);
        ArrayPool<DecoderInstruction>.Shared.Return(rented, true);
        return result;
    }

    private void EnsureRemainderCapacity(int needed)
    {
        if (_remainderBuffer is null || _remainderBuffer.Length < needed)
        {
            var newBuffer = new byte[Math.Max(needed, 256)];
            if (_remainderBuffer is not null && _remainderLength > 0)
            {
                _remainderBuffer.AsSpan(0, _remainderLength).CopyTo(newBuffer);
            }

            _remainderBuffer = newBuffer;
        }
    }

    private void StoreRemainder(ReadOnlySpan<byte> data)
    {
        EnsureRemainderCapacity(data.Length);
        data.CopyTo(_remainderBuffer);
        _remainderLength = data.Length;
    }
}
