using System.Buffers.Binary;
using System.Text;

namespace TurboHTTP.Protocol;

internal ref struct SpanWriter
{
    public static SpanWriter Create(Span<byte> buffer) => new(buffer);

    private Span<byte> _buffer;

    public int BytesWritten { get; private set; }

    public Span<byte> Remaining => _buffer;

    private SpanWriter(Span<byte> buffer)
    {
        _buffer = buffer;
        BytesWritten = 0;
    }

    public void Advance(int count)
    {
        _buffer = _buffer[count..];
        BytesWritten += count;
    }

    public void WriteBytes(ReadOnlySpan<byte> data)
    {
        data.CopyTo(_buffer);
        _buffer = _buffer[data.Length..];
        BytesWritten += data.Length;
    }

    public void WriteAscii(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        var written = Encoding.ASCII.GetBytes(value.AsSpan(), _buffer);
        _buffer = _buffer[written..];
        BytesWritten += written;
    }

    public void WriteAscii(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
        {
            return;
        }

        var written = Encoding.ASCII.GetBytes(value, _buffer);
        _buffer = _buffer[written..];
        BytesWritten += written;
    }

    public void WriteByte(byte value)
    {
        _buffer[0] = value;
        _buffer = _buffer[1..];
        BytesWritten++;
    }

    public void WriteUInt16BigEndian(ushort value)
    {
        BinaryPrimitives.WriteUInt16BigEndian(_buffer, value);
        _buffer = _buffer[2..];
        BytesWritten += 2;
    }

    public void WriteUInt32BigEndian(uint value)
    {
        BinaryPrimitives.WriteUInt32BigEndian(_buffer, value);
        _buffer = _buffer[4..];
        BytesWritten += 4;
    }

    public void WriteUInt24BigEndian(int value)
    {
        _buffer[0] = (byte)(value >> 16);
        _buffer[1] = (byte)(value >> 8);
        _buffer[2] = (byte)value;
        _buffer = _buffer[3..];
        BytesWritten += 3;
    }

    public void WriteCrlf() => WriteBytes(WellKnownHeaders.Crlf);
    public void WriteSpace() => WriteBytes(WellKnownHeaders.Space);
    public void WriteColonSpace() => WriteBytes(WellKnownHeaders.ColonSpace);

    public void WriteStatusCode(int statusCode)
    {
        _buffer[0] = (byte)('0' + statusCode / 100);
        _buffer[1] = (byte)('0' + statusCode / 10 % 10);
        _buffer[2] = (byte)('0' + statusCode % 10);

        _buffer = _buffer[3..];
        BytesWritten += 3;
    }

    public void WriteInt(int value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        var length = 0;
        var temp = value;

        do
        {
            temp /= 10;
            length++;
        } while (temp > 0);

        var position = length;

        do
        {
            _buffer[--position] = (byte)('0' + value % 10);
            value /= 10;
        } while (value > 0);

        _buffer = _buffer[length..];
        BytesWritten += length;
    }

    public void WriteHex(int value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        if (value == 0)
        {
            _buffer[0] = (byte)'0';
            _buffer = _buffer[1..];
            BytesWritten++;
            return;
        }

        var temp = value;
        var length = 0;

        while (temp != 0)
        {
            temp >>= 4;
            length++;
        }

        var pos = length;

        while (value != 0)
        {
            var digit = value & 0xF;

            _buffer[--pos] =
                (byte)(digit < 10
                    ? '0' + digit
                    : 'a' + (digit - 10));

            value >>= 4;
        }

        _buffer = _buffer[length..];
        BytesWritten += length;
    }
}